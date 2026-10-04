using KidaGoSync.Connection;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

public class SyncStateCoordinatorTests
{
    private static readonly PdaDevice Pda = new("pda", "Pixel");
    private static readonly ConnectionStatus Connected = new(ConnectionState.Connected, [Pda], Pda);

    private TaskCompletionSource<ActionOutcome?> _importGate = new();
    private TaskCompletionSource<ActionOutcome?> _exportGate = new();
    private int _imports, _exports;

    private SyncStateCoordinator Coordinator(bool connected = true)
    {
        var c = new SyncStateCoordinator(
            () => { _imports++; return _importGate.Task; },
            () => { _exports++; return _exportGate.Task; });
        if (connected) c.OnConnectionChanged(Connected);
        return c;
    }

    [Fact]
    public async Task BothActionsAreUnavailableAndIgnoredWhileDisconnected()
    {
        var c = Coordinator(connected: false);
        Assert.False(c.CanImportar);
        Assert.False(c.CanExportar);
        await c.ImportarAsync();
        c.RequestExportar();
        Assert.Equal(SyncState.Disconnected, c.State);
        Assert.Equal(0, _imports);
    }

    [Fact]
    public void WhileChoosingADeviceTheActionsStayUnavailable()
    {
        var c = Coordinator(connected: false);
        c.OnConnectionChanged(new(ConnectionState.ChoosingDevice, [Pda, new("b", "B")], null));
        Assert.Equal(SyncState.ChoosingDevice, c.State);
        Assert.False(c.CanImportar);
    }

    [Fact]
    public async Task ConnectedEnablesBothAndEitherCanRunFirst()
    {
        var c = Coordinator();
        Assert.True(c.CanImportar && c.CanExportar);
        var run = c.ImportarAsync();
        _importGate.SetResult(new ActionOutcome(true));
        await run;
        Assert.Equal(1, _imports);
    }

    [Fact]
    public async Task OneActionRunsAtATimeAndTheOtherIsRefused()
    {
        var c = Coordinator();
        var importing = c.ImportarAsync();
        Assert.Equal(SyncState.Importing, c.State);
        Assert.False(c.CanImportar || c.CanExportar);
        c.RequestExportar(); // refused
        await c.ConfirmExportAsync(); // nothing to confirm
        await c.ImportarAsync(); // a second import is refused too
        Assert.Equal(SyncState.Importing, c.State);
        Assert.Equal(1, _imports);
        Assert.Equal(0, _exports);

        _importGate.SetResult(new ActionOutcome(true));
        await importing;
        Assert.Equal(SyncState.Connected, c.State);
    }

    [Fact]
    public async Task ExportarWaitsForConfirmationThenRunsAndBlocksImportar()
    {
        var c = Coordinator();
        c.RequestExportar();
        Assert.Equal(SyncState.ConfirmingExport, c.State);
        await c.ImportarAsync(); // blocked while the prompt is pending
        Assert.Equal(0, _imports);
        Assert.Equal(0, _exports);

        var exporting = c.ConfirmExportAsync();
        Assert.Equal(SyncState.Exporting, c.State);
        await c.ImportarAsync();
        Assert.Equal(0, _imports);
        _exportGate.SetResult(new ActionOutcome(true));
        await exporting;
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Equal(1, _exports);
    }

    [Fact]
    public void DecliningReturnsToConnectedWithNoSideEffect()
    {
        var c = Coordinator();
        c.RequestExportar();
        c.DeclineExport();
        Assert.Equal(SyncState.Connected, c.State);
        Assert.Equal(0, _exports);
        Assert.Null(c.LastOutcome);
    }

    [Fact]
    public async Task EachActionShowsItsOutcomeSuccessOrFailure()
    {
        var c = Coordinator();
        var run = c.ImportarAsync();
        _importGate.SetResult(new ActionOutcome(true));
        await run;
        Assert.Equal(new ShownOutcome(ActionKind.Importar, new ActionOutcome(true)), c.LastOutcome);

        c.RequestExportar();
        Assert.Null(c.LastOutcome); // the old outcome goes when the next action begins
        var export = c.ConfirmExportAsync();
        _exportGate.SetResult(new ActionOutcome(false, "missing"));
        await export;
        Assert.Equal(new ShownOutcome(ActionKind.Exportar, new ActionOutcome(false, "missing")), c.LastOutcome);
    }

    [Fact]
    public async Task ACancelledActionShowsNoOutcome()
    {
        var c = Coordinator();
        c.RequestExportar();
        var export = c.ConfirmExportAsync();
        _exportGate.SetResult(null);
        await export;
        Assert.Null(c.LastOutcome);
        Assert.Equal(SyncState.Connected, c.State);
    }

    [Fact]
    public async Task AnActionThatThrowsIsAFailureNotAStuckPanel()
    {
        var c = new SyncStateCoordinator(() => throw new InvalidOperationException("boom"), () => Task.FromResult<ActionOutcome?>(null));
        c.OnConnectionChanged(Connected);
        await c.ImportarAsync();
        Assert.Equal(SyncState.Connected, c.State);
        Assert.False(c.LastOutcome!.Outcome.Success);
    }

    [Fact]
    public async Task DisconnectingMidActionDoesNotInterruptItAndTheStateFollowsAfterwards()
    {
        var c = Coordinator();
        var run = c.ImportarAsync();
        c.OnConnectionChanged(ConnectionStatus.Disconnected);
        Assert.Equal(SyncState.Importing, c.State);
        _importGate.SetResult(new ActionOutcome(false, "gone"));
        await run;
        Assert.Equal(SyncState.Disconnected, c.State);
        Assert.False(c.CanImportar);
    }
}
