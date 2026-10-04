using System.IO;
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
    private readonly FakeStorage _storage = new();
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private CatalogSender Sender(string? picked, PdaDevice? device = null) =>
        new(() => picked, () => device ?? Pda, _storage);

    [Fact]
    public async Task TheChosenFileIsSentUnchangedUnderItsDatedNameToTheFixedFolder()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, (byte)'1', (byte)'\r', (byte)'\n', 0, 255]; // BOM, CRLF, NUL: nothing may be normalized
        var path = Write("product-catalog-2026-10-04.txt", content);

        var outcome = await Sender(path).SendAsync();

        Assert.Equal(new ActionOutcome(true), outcome);
        var sent = Assert.Single(_storage.Sent);
        Assert.Equal(Pda, sent.Device);
        Assert.Equal(content, sent.Content);
        Assert.Equal("afede/kidago", sent.Folder);
        Assert.Equal("product-catalog-2026-10-04.txt", sent.Name);
    }

    [Fact]
    public async Task NothingIsValidatedSoAnyFileGoesThrough()
    {
        var path = Write("whatever.bin", [1, 2, 3]);
        Assert.True((await Sender(path).SendAsync())!.Success);
        Assert.Equal("whatever.bin", _storage.Sent[0].Name);
    }

    [Fact]
    public async Task SendingTheSameNameAgainIsPassedOnSoTheStorageOverwrites()
    {
        var path = Write("product-catalog-2026-10-04.txt", [1]);
        await Sender(path).SendAsync();
        File.WriteAllBytes(path, [2]);
        await Sender(path).SendAsync();
        Assert.Equal([2], _storage.Sent[1].Content);
        Assert.Equal(_storage.Sent[0].Name, _storage.Sent[1].Name);
    }

    [Fact]
    public async Task CancellingThePickerSendsNothingAndReportsNothing()
    {
        Assert.Null(await Sender(null).SendAsync());
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public async Task AFailedSendIsReportedAsAFailure()
    {
        _storage.Fails = new IOException("disco lleno");
        var outcome = await Sender(Write("a.txt", [1])).SendAsync();
        Assert.False(outcome!.Success);
        Assert.Equal("disco lleno", outcome.Detail);
    }

    [Fact]
    public async Task NoPdaAtSendTimeIsAFailureAndNothingIsSent()
    {
        var sender = new CatalogSender(() => Write("a.txt", [1]), () => null, _storage);
        Assert.False((await sender.SendAsync())!.Success);
        Assert.Empty(_storage.Sent);
    }

    [Fact]
    public void ThePickerOpensAtTheFixedDesktopFolder() => Assert.Equal(@"C:\afede\kidago", CatalogSender.PickerFolder);
}
