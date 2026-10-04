using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KidaGoSync.Connection;
using KidaGoSync.Settings;
using KidaGoSync.Sync;

namespace KidaGoSync;

/// <summary>Panel principal. Code-behind only (Project Architecture): it renders the coordinator's state and forwards clicks.</summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ConnectionMonitor _monitor = new(new MediaDevicesMtpSource());
    private readonly SyncStateCoordinator _coordinator;
    private readonly CancellationTokenSource _closing = new();
    private readonly PanelSettings _settings = new(PanelSettings.DefaultFile);
    private bool _refreshingChooser;
    private SyncState _lastState;

    public MainWindow()
    {
        InitializeComponent();
        var storage = new MediaDevicesPdaStorage();
        var sender = new CatalogSender(PickCatalogFile, () => _monitor.Status.Selected, storage);
        var retriever = new ListRetriever(() => _monitor.Status.Selected, storage, () => _settings.ScannedListLocation, PickSavePath, ShowPulledText);
        var checkFlow = new CatalogCheckFlow(PickCatalogFile, PickCorrectedCatalogPath);
        _coordinator = new SyncStateCoordinator(sender.SendAsync, retriever.RetrieveAsync, checkFlow);
        _coordinator.Changed += Render;
        _monitor.Changed += status =>
        {
            _coordinator.OnConnectionChanged(status);
            RefreshChooser(status);
        };

        ImportarButton.Content = PanelText.Importar;
        ExportarButton.Content = PanelText.Exportar;
        ComprobarButton.Content = PanelText.Comprobar;
        ConfirmTitle.Text = PanelText.ConfirmExportTitle;
        ConfirmBody.Text = PanelText.ConfirmExportBody;
        ConfirmButton.Content = PanelText.ConfirmExportConfirm;
        DeclineButton.Content = PanelText.ConfirmExportDecline;
        PathLabel.Text = PanelText.PathLabel;
        TxtLabel.Text = PanelText.TxtViewLabel;
        PathBox.Text = _settings.ScannedListLocation;
        Render();

        Loaded += async (_, _) => await _monitor.RunAsync(PollInterval, _closing.Token); // continues on the UI thread
        Closed += (_, _) => _closing.Cancel();
    }

    /// <summary>The native open dialog, always starting in the fixed catalog folder; null when cancelled.</summary>
    private string? PickCatalogFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = CatalogSender.PickerFolder,
            Filter = "Catálogo (*.txt)|*.txt|Todos los archivos|*.*", // placeholder copy, see PanelText
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>The native save dialog for the corrected catalog, in the catalog folder and proposing the original name; null when cancelled.</summary>
    private string? PickCorrectedCatalogPath(string proposedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = CatalogCheckFlow.PickerFolder,
            FileName = proposedName,
            OverwritePrompt = true, // an overwrite happens only through the system's own confirmation (C18)
            Filter = "Catálogo (*.txt)|*.txt|Todos los archivos|*.*", // placeholder copy, see PanelText
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>Saves the edited location; an empty one is refused and the previous value comes back (C10).</summary>
    private void OnPathCommit(object sender, RoutedEventArgs e)
    {
        _settings.TrySetScannedListLocation(PathBox.Text);
        PathBox.Text = _settings.ScannedListLocation;
    }

    private void OnPathKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) OnPathCommit(sender, e);
    }

    /// <summary>Shows a pulled list as raw text; it stays until the next action begins (C11).</summary>
    public void ShowPulledText(string rawText)
    {
        TxtView.Text = PanelText.TxtViewContent(rawText);
        TxtLabel.Visibility = TxtCard.Visibility = Visibility.Visible;
    }

    private void HideTxtView() => TxtLabel.Visibility = TxtCard.Visibility = Visibility.Collapsed;

    /// <summary>The native save dialog, always starting in the fixed local folder; null when cancelled.</summary>
    private string? PickSavePath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = ListRetriever.SaveFolder,
            FileName = System.IO.Path.GetFileName(_settings.ScannedListLocation),
            Filter = "Texto (*.txt)|*.txt|Todos los archivos|*.*", // placeholder copy, see PanelText
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void OnImportar(object sender, RoutedEventArgs e) => await _coordinator.ImportarAsync();

    private async void OnComprobar(object sender, RoutedEventArgs e) => await _coordinator.ComprobarAsync();

    private void OnExportar(object sender, RoutedEventArgs e) => _coordinator.RequestExportar();

    private async void OnConfirmExport(object sender, RoutedEventArgs e) => await _coordinator.ConfirmExportAsync();

    private void OnDeclineExport(object sender, RoutedEventArgs e) => _coordinator.DeclineExport();

    private void OnDeviceChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingChooser && DeviceChooser.SelectedItem is PdaDevice device) _monitor.Select(device.Id);
    }

    private void RefreshChooser(ConnectionStatus status)
    {
        _refreshingChooser = true;
        DeviceChooser.ItemsSource = status.Devices;
        DeviceChooser.SelectedItem = status.Selected;
        DeviceChooser.Visibility = status.Devices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _refreshingChooser = false;
    }

    private void Render()
    {
        var state = _coordinator.State;
        if (state != _lastState && state is SyncState.Importing or SyncState.ConfirmingExport or SyncState.Exporting) HideTxtView(); // a new action began
        _lastState = state;
        var connected = _coordinator.PdaConnected;
        Paint(StatusBadge, StatusGlyph, connected ? "SuccessBrush" : "TextBrush", connected ? "✓" : "–");
        StatusText.Text = state switch
        {
            SyncState.ChoosingDevice => PanelText.ChoosingDevice,
            _ => connected ? PanelText.Connected : PanelText.Disconnected,
        };

        ImportarButton.IsEnabled = _coordinator.CanImportar;
        ExportarButton.IsEnabled = _coordinator.CanExportar;
        ComprobarButton.IsEnabled = _coordinator.CanComprobar;
        if (state == SyncState.ReviewingCheck && _coordinator.Review is { } review)
        {
            if (CheckCard.Visibility != Visibility.Visible)
            {
                if (_coordinator.ReviewFor == ReviewPurpose.Import)
                    CheckCard.Show(review.Result, PanelText.CheckNormalizeImport, offerConfirm: !review.Result.HasNoValidLine, _coordinator.ConfirmNormalize, _coordinator.DismissReview);
                else
                    CheckCard.Show(review.Result, PanelText.CheckCorrect, offerConfirm: true, async () => await _coordinator.CorrectAsync(), _coordinator.DismissReview);
            }
            CheckCard.Visibility = Visibility.Visible;
        }
        else CheckCard.Visibility = Visibility.Collapsed;
        ImportarButton.Content = state == SyncState.Importing ? PanelText.Working : PanelText.Importar;
        ExportarButton.Content = state == SyncState.Exporting ? PanelText.Working : PanelText.Exportar;
        ConfirmCard.Visibility = state == SyncState.ConfirmingExport ? Visibility.Visible : Visibility.Collapsed;

        var shown = _coordinator.LastOutcome;
        OutcomeBlock.Visibility = shown is null ? Visibility.Collapsed : Visibility.Visible;
        if (shown is null) return;
        var ok = shown.Outcome.Success;
        Paint(OutcomeBadge, OutcomeGlyph, ok ? "SuccessBrush" : "AccentBrush", ok ? "✓" : "!");
        OutcomeHeading.Text = (shown.Kind, ok) switch
        {
            (ActionKind.Comprobar, true) => PanelText.ComprobarSuccess,
            (ActionKind.Comprobar, false) => PanelText.ComprobarFailure,
            (ActionKind.Importar, true) => PanelText.ImportarSuccess,
            (ActionKind.Importar, false) => PanelText.ImportarFailure,
            (_, true) => PanelText.ExportarSuccess,
            _ => PanelText.ExportarFailure,
        };
        OutcomeDetail.Text = shown.Outcome.Detail ?? "";
        OutcomeDetail.Visibility = string.IsNullOrEmpty(shown.Outcome.Detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A coloured badge with a glyph: a distinct shape (✓, !, –) as well as a colour.</summary>
    private void Paint(Border badge, TextBlock glyph, string brushKey, string text)
    {
        badge.Background = (Brush)FindResource(brushKey);
        glyph.Text = text;
    }
}
