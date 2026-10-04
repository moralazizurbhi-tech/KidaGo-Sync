using KidaGoSync.Connection;

namespace KidaGoSync.Sync;

public enum SyncState
{
    Disconnected,
    ChoosingDevice,
    Connected,
    Importing,
    ConfirmingExport,
    Exporting,
}

public enum ActionKind { Importar, Exportar }

/// <summary>What an action reports when it finishes. <see cref="Detail"/> is the failure reason, when there is one.</summary>
public sealed record ActionOutcome(bool Success, string? Detail = null);

/// <summary>The last finished action and how it went, as the panel shows it (C9).</summary>
public sealed record ShownOutcome(ActionKind Kind, ActionOutcome Outcome);

/// <summary>
/// Owns the panel's state and enforces one action at a time (FEAT-005 TD, SyncStateCoordinator). Importar and
/// Exportar are only accepted from Connected (C2); while one is running, or Exportar waits for its confirmation,
/// every other request is ignored (C8). The actions themselves are injected: the catalog send and the list retrieval.
/// An action returning null finished with nothing to report (a cancelled save, C12). Single-threaded: call from the UI
/// thread, and <see cref="Changed"/> is raised there too.
/// </summary>
public sealed class SyncStateCoordinator
{
    private readonly Func<Task<ActionOutcome?>> _import;
    private readonly Func<Task<ActionOutcome?>> _export;
    private ConnectionStatus _connection = ConnectionStatus.Disconnected;
    private SyncState? _busy;

    public SyncStateCoordinator(Func<Task<ActionOutcome?>> import, Func<Task<ActionOutcome?>> export)
    {
        _import = import;
        _export = export;
    }

    public SyncState State => _busy ?? _connection.State switch
    {
        ConnectionState.Connected => SyncState.Connected,
        ConnectionState.ChoosingDevice => SyncState.ChoosingDevice,
        _ => SyncState.Disconnected,
    };

    /// <summary>The outcome of the last action; cleared when the next action starts.</summary>
    public ShownOutcome? LastOutcome { get; private set; }

    public bool CanImportar => State == SyncState.Connected;
    public bool CanExportar => State == SyncState.Connected;

    public event Action? Changed;

    /// <summary>The connection monitor reported a change. A running action is not interrupted by it.</summary>
    public void OnConnectionChanged(ConnectionStatus status)
    {
        _connection = status;
        Changed?.Invoke();
    }

    public async Task ImportarAsync()
    {
        if (!CanImportar) return;
        await RunAsync(SyncState.Importing, ActionKind.Importar, _import);
    }

    /// <summary>Exportar was clicked: ask for confirmation, and do nothing else until it is answered (C4, C6).</summary>
    public void RequestExportar()
    {
        if (!CanExportar) return;
        LastOutcome = null;
        _busy = SyncState.ConfirmingExport;
        Changed?.Invoke();
    }

    public void DeclineExport()
    {
        if (_busy != SyncState.ConfirmingExport) return;
        _busy = null;
        Changed?.Invoke();
    }

    public async Task ConfirmExportAsync()
    {
        if (_busy != SyncState.ConfirmingExport) return;
        await RunAsync(SyncState.Exporting, ActionKind.Exportar, _export);
    }

    private async Task RunAsync(SyncState running, ActionKind kind, Func<Task<ActionOutcome?>> action)
    {
        LastOutcome = null;
        _busy = running;
        Changed?.Invoke();
        ActionOutcome? outcome;
        try
        {
            outcome = await action();
        }
        catch (Exception e)
        {
            outcome = new ActionOutcome(false, e.Message); // an action that throws is a failed action, never a stuck panel
        }
        _busy = null;
        LastOutcome = outcome is null ? null : new ShownOutcome(kind, outcome);
        Changed?.Invoke();
    }
}
