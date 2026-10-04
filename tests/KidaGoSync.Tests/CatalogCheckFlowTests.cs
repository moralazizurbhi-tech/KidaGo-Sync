using System.IO;
using System.Text;
using KidaGoSync.Connection;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

/// <summary>Comprobar catálogo: the flow itself and how the coordinator holds the panel while it runs (FEAT-005 C16, C18, C20).</summary>
public class CatalogCheckFlowTests : IDisposable
{
    private static readonly PdaDevice Pda = new("pda", "Pixel");
    private static readonly ConnectionStatus Connected = new(ConnectionState.Connected, [Pda], Pda);

    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string? _picked;
    private string? _savePath;
    private string? _proposed;
    private int _saveDialogs;
    private int _imports, _exports;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Catalog(string text, string name = "product-catalog-2026-10-04.txt")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return _picked = path;
    }

    private CatalogCheckFlow Flow() => new(() => _picked, name => { _saveDialogs++; _proposed = name; return _savePath; });

    private SyncStateCoordinator Coordinator(bool connected)
    {
        var c = new SyncStateCoordinator(
            _ => { _imports++; return Task.FromResult<ActionOutcome?>(new ActionOutcome(true)); },
            () => { _exports++; return Task.FromResult<ActionOutcome?>(new ActionOutcome(true)); },
            Flow());
        if (connected) c.OnConnectionChanged(Connected);
        return c;
    }

    private const string WithProblems = "8601153100055\n012345678905\nbad\n";

    [Fact]
    public async Task AnalysisWritesNothingAndLeavesTheOriginalUntouched()
    {
        var path = Catalog(WithProblems);
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        Assert.Equal(SyncState.ReviewingCheck, c.State);
        Assert.False(c.Review!.Result.IsClean);
        Assert.Equal(0, _saveDialogs);
        Assert.Equal([Path.GetFileName(path)], Directory.GetFiles(_dir).Select(Path.GetFileName)); // no second file
        Assert.Equal(WithProblems, File.ReadAllText(path));
    }

    [Fact]
    public async Task CheckRunsWithNoPdaConnected()
    {
        Catalog(WithProblems);
        var c = Coordinator(connected: false);
        Assert.True(c.CanComprobar);
        await c.ComprobarAsync();
        Assert.Equal(SyncState.ReviewingCheck, c.State);
        Assert.False(c.PdaConnected);
    }

    [Fact]
    public async Task CorrigirSavesTheCorrectedCopyProposingTheOriginalNameAndNeverTouchesTheOriginal()
    {
        var path = Catalog(WithProblems);
        _savePath = Path.Combine(_dir, "copy.txt");
        var c = Coordinator(connected: false);
        await c.ComprobarAsync();
        await c.CorrectAsync();
        Assert.Equal("product-catalog-2026-10-04.txt", _proposed);
        Assert.Equal("8601153100055\n0012345678905\n", File.ReadAllText(_savePath));
        Assert.Equal(WithProblems, File.ReadAllText(path));
        Assert.Equal(new ShownOutcome(ActionKind.Comprobar, new ActionOutcome(true, _savePath)), c.LastOutcome);
        Assert.Equal(SyncState.Disconnected, c.State);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task CancellingTheSavePickerSavesNothing()
    {
        Catalog(WithProblems);
        _savePath = null;
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        await c.CorrectAsync();
        Assert.Equal(1, _saveDialogs);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Null(c.LastOutcome);
        Assert.Equal(SyncState.Connected, c.State);
    }

    [Fact]
    public async Task DismissingTheSummarySavesNothingAndFreesThePanel()
    {
        Catalog(WithProblems);
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        c.DismissReview();
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Equal(0, _saveDialogs);
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task WhenNoValidLineRemainsNothingIsSavedAndTheUserIsTold()
    {
        Catalog("bad\n4444\n12345678901234\n");
        _savePath = Path.Combine(_dir, "copy.txt");
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        await c.CorrectAsync();
        Assert.False(File.Exists(_savePath));
        Assert.Equal(0, _saveDialogs); // not even the picker
        Assert.False(c.LastOutcome!.Outcome.Success);
        Assert.Contains("ninguna línea válida", c.LastOutcome.Outcome.Detail);
    }

    [Fact]
    public async Task ACleanFileOffersNothingToCorrect()
    {
        Catalog("8601153100055\n");
        _savePath = Path.Combine(_dir, "copy.txt");
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        Assert.True(c.Review!.Result.IsClean);
        await c.CorrectAsync();
        Assert.Equal(SyncState.ReviewingCheck, c.State); // ignored: still waiting for the user to close the summary
        Assert.False(File.Exists(_savePath));
    }

    [Fact]
    public async Task CancellingTheOpenPickerEndsTheCheckWithNothingShown()
    {
        _picked = null;
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Null(c.Review);
        Assert.Null(c.LastOutcome);
    }

    [Fact]
    public async Task AnUnreadableFileIsAFailedCheckNotAStuckPanel()
    {
        _picked = Path.Combine(_dir, "missing.txt");
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        Assert.Equal(SyncState.Connected, c.State);
        Assert.False(c.LastOutcome!.Outcome.Success);
        Assert.Equal(ActionKind.Comprobar, c.LastOutcome.Kind);
    }

    [Fact]
    public async Task WhileASummaryIsShowingNoOtherActionCanBeTriggered()
    {
        Catalog(WithProblems);
        var c = Coordinator(connected: true);
        await c.ComprobarAsync();
        Assert.False(c.CanImportar || c.CanExportar || c.CanComprobar);
        await c.ImportarAsync();
        c.RequestExportar();
        await c.ComprobarAsync();
        Assert.Equal((0, 0), (_imports, _exports));
        Assert.Equal(SyncState.ReviewingCheck, c.State);
    }

    [Fact]
    public async Task ImportarAndExportarBlockAComprobar()
    {
        Catalog(WithProblems);
        var c = Coordinator(connected: true);
        c.RequestExportar();
        Assert.False(c.CanComprobar);
        await c.ComprobarAsync();
        Assert.Equal(SyncState.ConfirmingExport, c.State);
        Assert.Null(c.Review);
    }

    [Fact]
    public async Task WhileAnalyzingTheOtherActionsAreUnavailable()
    {
        Catalog(WithProblems);
        var gate = new TaskCompletionSource();
        var c = new SyncStateCoordinator(
            _ => Task.FromResult<ActionOutcome?>(null), () => Task.FromResult<ActionOutcome?>(null),
            new CatalogCheckFlow(() => { gate.Task.Wait(); return _picked; }, _ => null));
        c.OnConnectionChanged(Connected);
        var running = Task.Run(() => c.ComprobarAsync()); // the picker blocks; State is read only after it started
        while (c.State != SyncState.Checking) await Task.Delay(5);
        Assert.False(c.CanImportar || c.CanExportar || c.CanComprobar);
        gate.SetResult();
        await running;
        Assert.Equal(SyncState.ReviewingCheck, c.State);
    }

    // ---- Importar's own summary (C19): the same dialog, held by the coordinator while Importar waits

    private static CatalogAnalysis Analysis(string text) => new("product-catalog-2026-10-04.txt", CatalogChecker.Check(text), []);

    private SyncStateCoordinator ImportingCoordinator(CatalogAnalysis analysis, List<bool> answers)
    {
        var c = new SyncStateCoordinator(
            async ask => { var answer = await ask(analysis); answers.Add(answer); return answer ? new ActionOutcome(true) : null; },
            () => Task.FromResult<ActionOutcome?>(null),
            Flow());
        c.OnConnectionChanged(Connected);
        return c;
    }

    [Fact]
    public async Task ImportarWithProblemsShowsTheSummaryHoldsThePanelAndNormalizeContinues()
    {
        var answers = new List<bool>();
        var c = ImportingCoordinator(Analysis(WithProblems), answers);
        var running = c.ImportarAsync();
        Assert.Equal(SyncState.ReviewingCheck, c.State);
        Assert.Equal(ReviewPurpose.Import, c.ReviewFor);
        Assert.NotNull(c.Review);
        Assert.False(c.CanImportar || c.CanExportar || c.CanComprobar);
        await c.CorrectAsync(); // Corregir belongs to Comprobar: ignored here
        Assert.Equal(SyncState.ReviewingCheck, c.State);

        c.ConfirmNormalize();
        await running;

        Assert.Equal([true], answers);
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Null(c.Review);
        Assert.Equal(new ShownOutcome(ActionKind.Importar, new ActionOutcome(true)), c.LastOutcome);
    }

    [Fact]
    public async Task CancellingImportarsSummarySendsNothingAndFreesThePanel()
    {
        var answers = new List<bool>();
        var c = ImportingCoordinator(Analysis(WithProblems), answers);
        var running = c.ImportarAsync();
        c.DismissReview();
        await running;
        Assert.Equal([false], answers);
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Null(c.LastOutcome);
    }

    [Fact]
    public async Task WhenNoValidLineRemainsNormalizeIsIgnoredAndOnlyCancelWorks()
    {
        var answers = new List<bool>();
        var c = ImportingCoordinator(Analysis("bad\n4444\n"), answers);
        var running = c.ImportarAsync();
        c.ConfirmNormalize();
        Assert.Equal(SyncState.ReviewingCheck, c.State);
        Assert.Empty(answers);
        c.DismissReview();
        await running;
        Assert.Equal([false], answers);
    }
}
