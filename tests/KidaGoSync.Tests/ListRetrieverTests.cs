using System.IO;
using System.Text;
using KidaGoSync.Connection;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

public class ListRetrieverTests
{
    private sealed class FakeStorage(List<string> log, Func<string> localFile) : IPdaStorage
    {
        public byte[]? Content { get; set; }
        public Exception? ReadFails { get; set; }
        public Exception? EmptyFails { get; set; }
        public string? ReadPath { get; private set; }
        public bool LocalCopyExistedWhenEmptied { get; private set; }

        public Task SendFileAsync(PdaDevice device, string localPath, string folder, string fileName) => throw new NotSupportedException();

        public Task<byte[]?> ReadFileAsync(PdaDevice device, string path)
        {
            log.Add("read");
            ReadPath = path;
            return ReadFails is null ? Task.FromResult(Content) : throw ReadFails;
        }

        public Task EmptyFileAsync(PdaDevice device, string path)
        {
            log.Add("empty");
            LocalCopyExistedWhenEmptied = File.Exists(localFile());
            return EmptyFails is null ? Task.CompletedTask : throw EmptyFails;
        }
    }

    private static readonly PdaDevice Pda = new("pda", "Pixel");
    private readonly List<string> _log = [];
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string? _picked;
    private string? _shown;
    private readonly FakeStorage _storage;

    public ListRetrieverTests()
    {
        _picked = Path.Combine(_dir, "lista.txt");
        _storage = new FakeStorage(_log, () => _picked ?? "");
    }

    private ListRetriever Retriever(string location = "afede/kidago/scanned-list.txt", bool hasDevice = true) =>
        new(() => hasDevice ? Pda : null, _storage, () => location,
            () => { _log.Add("pick"); return _picked; },
            text => { _log.Add("show"); _shown = text; });

    [Fact]
    public async Task PullsSavesShowsAndOnlyThenEmptiesThePdaFile()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("111,2\n222,1\n");
        _storage.Content = bytes;

        var outcome = await Retriever().RetrieveAsync();

        Assert.Equal(new ActionOutcome(true), outcome);
        Assert.Equal(["read", "pick", "show", "empty"], _log);
        Assert.Equal(bytes, File.ReadAllBytes(_picked!));
        Assert.Equal("111,2\n222,1\n", _shown);
        Assert.True(_storage.LocalCopyExistedWhenEmptied); // the local copy was already on disk when the PDA was emptied
    }

    [Fact]
    public async Task ReadsFromTheConfiguredLocation()
    {
        _storage.Content = [];
        await Retriever(location: "otra/ruta/lista.txt").RetrieveAsync();
        Assert.Equal("otra/ruta/lista.txt", _storage.ReadPath);
    }

    [Fact]
    public async Task AMissingListFileIsAFailureAndNothingIsClearedOrAsked()
    {
        _storage.Content = null;
        var outcome = await Retriever().RetrieveAsync();
        Assert.False(outcome!.Success);
        Assert.Equal(["read"], _log); // no picker, no view, no clearing
        Assert.Null(_shown);
    }

    [Fact]
    public async Task CancellingTheSaveAbortsWithNothingShownAndThePdaFileUntouched()
    {
        _storage.Content = [1];
        _picked = null;
        Assert.Null(await Retriever().RetrieveAsync());
        Assert.Equal(["read", "pick"], _log);
        Assert.Null(_shown);
    }

    [Fact]
    public async Task AFailedLocalSaveLeavesThePdaListAlone()
    {
        _storage.Content = [1];
        _picked = Path.Combine(_dir, "no-such-folder", "lista.txt");
        var outcome = await Retriever().RetrieveAsync();
        Assert.False(outcome!.Success);
        Assert.DoesNotContain("empty", _log);
        Assert.Null(_shown);
    }

    [Fact]
    public async Task AnEmptyListIsNotAnErrorAndStillFeedsTheView()
    {
        _storage.Content = [];
        var outcome = await Retriever().RetrieveAsync();
        Assert.True(outcome!.Success);
        Assert.Equal("", _shown);
        Assert.Equal(PanelText.EmptyListMessage, PanelText.TxtViewContent(_shown!)); // the view shows its empty message
        Assert.Equal(["read", "pick", "show", "empty"], _log);
    }

    [Fact]
    public async Task AReadErrorIsAFailureThatClearsNothing()
    {
        _storage.ReadFails = new IOException("sin acceso");
        var outcome = await Retriever().RetrieveAsync();
        Assert.Equal("sin acceso", outcome!.Detail);
        Assert.Equal(["read"], _log);
    }

    [Fact]
    public async Task IfEmptyingFailsTheCopyStaysAndTheFailureIsReported()
    {
        _storage.Content = [1];
        _storage.EmptyFails = new IOException("ocupado");
        var outcome = await Retriever().RetrieveAsync();
        Assert.False(outcome!.Success);
        Assert.Contains("ocupado", outcome.Detail);
        Assert.True(File.Exists(_picked!));
    }

    [Fact]
    public async Task NoPdaAtRetrievalTimeIsAFailureAndNothingHappens()
    {
        var outcome = await Retriever(hasDevice: false).RetrieveAsync();
        Assert.False(outcome!.Success);
        Assert.Empty(_log);
    }

    [Fact]
    public void TheSavePickerOpensAtTheFixedDesktopFolder() => Assert.Equal(@"C:\afede\kidago", ListRetriever.SaveFolder);
}
