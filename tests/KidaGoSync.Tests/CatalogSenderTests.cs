using System.IO;
using System.Text;
using KidaGoSync.Connection;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

public class CatalogSenderTests
{
    private sealed class FakeStorage : IPdaStorage
    {
        public List<(PdaDevice Device, byte[] Content, string Folder, string Name)> Sent { get; } = [];
        public Exception? Fails { get; set; }

        public Task SendFileAsync(PdaDevice device, string localPath, string folder, string fileName)
        {
            if (Fails is not null) throw Fails;
            Sent.Add((device, File.ReadAllBytes(localPath), folder, fileName));
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadFileAsync(PdaDevice device, string path) => throw new NotSupportedException();
        public Task EmptyFileAsync(PdaDevice device, string path) => throw new NotSupportedException();
    }

    private static readonly PdaDevice Pda = new("pda", "Pixel");
    private const string Clean = "8601153100055\n";
    private const string WithProblems = "8601153100055\n012345678905\nbad\n";
    private readonly FakeStorage _storage = new();
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private readonly List<CatalogAnalysis> _asked = [];

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string Write(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    private CatalogSender Sender(string? picked, PdaDevice? device = null) =>
        new(() => picked, () => device ?? Pda, _storage);

    private Func<CatalogAnalysis, Task<bool>> Answer(bool normalize) => analysis => { _asked.Add(analysis); return Task.FromResult(normalize); };

    [Fact]
    public async Task ACleanFileIsSentUnchangedWithNoExtraStepUnderItsDatedNameToTheFixedFolder()
    {
        byte[] content = [.. new UTF8Encoding(true).GetPreamble(), .. "8601153100055\r\n8601153100062\r\n"u8.ToArray()]; // BOM and CRLF stay
        var path = Write("product-catalog-2026-10-04.txt", content);

        var outcome = await Sender(path).SendAsync(Answer(true));

        Assert.Equal(new ActionOutcome(true), outcome);
        Assert.Empty(_asked);
        var sent = Assert.Single(_storage.Sent);
        Assert.Equal(Pda, sent.Device);
        Assert.Equal(content, sent.Content);
        Assert.Equal("afede/kidago", sent.Folder);
        Assert.Equal("product-catalog-2026-10-04.txt", sent.Name);
    }

    [Fact]
    public async Task AFileWithProblemsIsNeverSentAsIsAndNormalizingSendsTheCorrectedLinesUnderTheOriginalName()
    {
        var path = Write("product-catalog-2026-10-04.txt", WithProblems);

        var outcome = await Sender(path).SendAsync(Answer(true));

        Assert.Equal(new ActionOutcome(true), outcome);
        Assert.False(_asked.Single().Result.IsClean);
        var sent = Assert.Single(_storage.Sent);
        Assert.Equal("8601153100055\n0012345678905\n", Encoding.UTF8.GetString(sent.Content));
        Assert.Equal("product-catalog-2026-10-04.txt", sent.Name);
        Assert.Equal(WithProblems, File.ReadAllText(path)); // the original on the computer is untouched
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "kidago-*.txt")); // the temporary copy is gone
    }

    [Fact]
    public async Task CancellingTheSummarySendsNothing()
    {
        var path = Write("product-catalog-2026-10-04.txt", WithProblems);
        Assert.Null(await Sender(path).SendAsync(Answer(false)));
        Assert.Empty(_storage.Sent);
        Assert.Equal(WithProblems, File.ReadAllText(path));
    }

    [Fact]
    public async Task WhenNoValidLineRemainsNothingIsSentEvenIfTheAnswerIsYes()
    {
        var path = Write("product-catalog-2026-10-04.txt", "bad\n4444\n");
        Assert.Null(await Sender(path).SendAsync(Answer(true)));
        Assert.Empty(_storage.Sent);
        Assert.True(_asked.Single().Result.HasNoValidLine);
    }

    [Fact]
    public async Task AnEmptyFileIsNotCleanSoItIsNeverSentAsIs()
    {
        var path = Write("product-catalog-2026-10-04.txt", "");
        Assert.Null(await Sender(path).SendAsync(Answer(true)));
        Assert.Empty(_storage.Sent);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task SendingTheSameNameAgainIsPassedOnSoTheStorageOverwrites()
    {
        var path = Write("product-catalog-2026-10-04.txt", Clean);
        await Sender(path).SendAsync(Answer(true));
        File.WriteAllText(path, "8601153100062\n");
        await Sender(path).SendAsync(Answer(true));
        Assert.Equal("8601153100062\n", Encoding.UTF8.GetString(_storage.Sent[1].Content));
        Assert.Equal(_storage.Sent[0].Name, _storage.Sent[1].Name);
    }

    [Fact]
    public async Task CancellingThePickerSendsNothingAndReportsNothing()
    {
        Assert.Null(await Sender(null).SendAsync(Answer(true)));
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public async Task AFailedSendIsReportedAsAFailure()
    {
        _storage.Fails = new IOException("disco lleno");
        var outcome = await Sender(Write("a.txt", Clean)).SendAsync(Answer(true));
        Assert.False(outcome!.Success);
        Assert.Equal("disco lleno", outcome.Detail);
    }

    [Fact]
    public async Task AnUnreadableFileIsAFailureAndNothingIsSent()
    {
        var outcome = await Sender(Path.Combine(_dir, "missing.txt")).SendAsync(Answer(true));
        Assert.False(outcome!.Success);
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public async Task NoPdaAtSendTimeIsAFailureAndNothingIsSent()
    {
        var sender = new CatalogSender(() => Write("a.txt", Clean), () => null, _storage);
        Assert.False((await sender.SendAsync(Answer(true)))!.Success);
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public async Task APdaLostWhileTheSummaryWasOpenIsAFailureAndNothingIsSent()
    {
        PdaDevice? device = Pda;
        var sender = new CatalogSender(() => Write("a.txt", WithProblems), () => device, _storage);
        var outcome = await sender.SendAsync(_ => { device = null; return Task.FromResult(true); });
        Assert.False(outcome!.Success);
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public void ThePickerOpensAtTheFixedDesktopFolder() => Assert.Equal(@"C:\afede\kidago", CatalogSender.PickerFolder);
}
