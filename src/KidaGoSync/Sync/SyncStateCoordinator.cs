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
    /// <summary>A catalog file is being analyzed (C20).</summary>
    Checking,
    /// <summary>The check summary is showing and waits for the user's choice (C16, C19).</summary>
    ReviewingCheck,
}

/// <summary>Why the summary is showing: a Comprobar catálogo, or an Importar whose file has problems (C19).</summary>
public enum ReviewPurpose { Check, Import }

public enum ActionKind { Importar, Exportar, Comprobar }

/// <summary>What an action reports when it finishes. <see cref="Detail"/> is the failure reason, when there is one.</summary>
public sealed record ActionOutcome(bool Success, string? Detail = null);

/// <summary>The last finished action and how it went, as the panel shows it (C9).</summary>
public sealed record ShownOutcome(ActionKind Kind, ActionOutcome Outcome);

/// <summary>
/// Owns the panel's state and enforces one action at a time (FEAT-005 TD, SyncStateCoordinator). Importar and
/// Exportar are only accepted from Connected (C2); while one is running, or Exportar waits for its confirmation,
/// every other request is ignored (C8). The actions themselves are injected: the catalog send and the list retrieval.
/// An action returning null finished with nothing to report (a cancelled save, C12). Comprobar catálogo is the exception
/// to the connection rule: it runs from any resting state, also with no PDA (C20), and like the others it excludes
/// every other action from its start until its summary is answered. Single-threaded: call from the UI
/// thread, and <see cref="Changed"/> is raised there too.
/// </summary>
public sealed class SyncStateCoordinator
{
    private readonly Func<Func<CatalogAnalysis, Task<bool>>, Task<ActionOutcome?>> _import;
    private TaskCompletionSource<bool>? _importDecision;
    private readonly Func<Task<ActionOutcome?>> _export;
    private readonly CatalogCheckFlow _check;
    private ConnectionStatus _connection = ConnectionStatus.Disconnected;
    private SyncState? _busy;

    /// <param name="import">Runs Importar; it is handed the question to put to the user when the file has problems.</param>
    public SyncStateCoordinator(Func<Func<CatalogAnalysis, Task<bool>>, Task<ActionOutcome?>> import, Func<Task<ActionOutcome?>> export, CatalogCheckFlow check)
    {
        _import = import;
        _export = export;
        _check = check;
    }

    public SyncState State => _busy ?? _connection.State switch
    {
        ConnectionState.Connected => SyncState.Connected,
        ConnectionState.ChoosingDevice => SyncState.ChoosingDevice,
        _ => SyncState.Disconnected,
    };

    /// <summary>The outcome of the last action; cleared when the next action starts.</summary>
    public ShownOutcome? LastOutcome { get; private set; }

    /// <summary>The analyzed file whose summary is showing; set only while <see cref="SyncState.ReviewingCheck"/>.</summary>
    public CatalogAnalysis? Review { get; private set; }

    /// <summary>What the showing summary is for; meaningful only while <see cref="SyncState.ReviewingCheck"/>.</summary>
    public ReviewPurpose ReviewFor { get; private set; }

    /// <summary>Whether a PDA is connected, whatever action is running (the panel's status shows this).</summary>
    public bool PdaConnected => _connection.State == ConnectionState.Connected;

    public bool CanImportar => State == SyncState.Connected;
    public bool CanExportar => State == SyncState.Connected;
    public bool CanComprobar => _busy is null;

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
        await RunAsync(SyncState.Importing, ActionKind.Importar, () => _import(AskNormalizeAsync));
    }

    /// <summary>Importar found problems: show the summary and wait for "Normalizar e importar" or "Cancelar" (C19).</summary>
    private async Task<bool> AskNormalizeAsync(CatalogAnalysis analysis)
    {
        _importDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Review = analysis;
        ReviewFor = ReviewPurpose.Import;
        _busy = SyncState.ReviewingCheck;
        Changed?.Invoke();
        var normalize = await _importDecision.Task;
        _importDecision = null;
        Review = null;
        _busy = SyncState.Importing;
        Changed?.Invoke();
        return normalize;
    }

    /// <summary>"Normalizar e importar". Not offered, and ignored, when no valid line would remain.</summary>
    public void ConfirmNormalize()
    {
        if (_busy != SyncState.ReviewingCheck || ReviewFor != ReviewPurpose.Import || Review is null || Review.Result.HasNoValidLine) return;
        _importDecision?.TrySetResult(true);
    }

    /// <summary>
    /// Comprobar catálogo: pick and analyze a file, then wait in <see cref="SyncState.ReviewingCheck"/> for
    /// <see cref="CorrectAsync"/> or <see cref="DismissReview"/>. A cancelled picker ends it with nothing shown.
    /// </summary>
    public async Task ComprobarAsync()
    {
        if (!CanComprobar) return;
        LastOutcome = null;
        _busy = SyncState.Checking;
        Changed?.Invoke();
        try
        {
            Review = await _check.AnalyzeAsync();
            ReviewFor = ReviewPurpose.Check;
        }
        catch (Exception e)
        {
            LastOutcome = new ShownOutcome(ActionKind.Comprobar, new ActionOutcome(false, e.Message));
        }
        _busy = Review is null ? null : SyncState.ReviewingCheck;
        Changed?.Invoke();
    }

    /// <summary>"Corregir": save the corrected copy; the summary stays up until the save picker and write are done.</summary>
    public async Task CorrectAsync()
    {
        if (_busy != SyncState.ReviewingCheck || ReviewFor != ReviewPurpose.Check || Review is not { } review || review.Result.IsClean) return;
        ActionOutcome? outcome;
        try
        {
            outcome = await _check.SaveCorrectedAsync(review);
        }
        catch (Exception e)
        {
            outcome = new ActionOutcome(false, e.Message);
        }
        Review = null;
        _busy = null;
        LastOutcome = outcome is null ? null : new ShownOutcome(ActionKind.Comprobar, outcome);
        Changed?.Invoke();
    }

    /// <summary>"Cancelar" (or closing the summary): nothing is saved and the original is untouched.</summary>
    public void DismissReview()
    {
        if (_busy != SyncState.ReviewingCheck) return;
        if (ReviewFor == ReviewPurpose.Import)
        {
            _importDecision?.TrySetResult(false); // Importar carries on and sends nothing
            return;
        }
        Review = null;
        _busy = null;
        Changed?.Invoke();
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
