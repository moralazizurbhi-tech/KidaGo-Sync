using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

/// <summary>The summary dialog's content (FEAT-005 C21), built on an STA thread with the real theme.</summary>
public class CheckSummaryCardTests
{
    private static void Sta(Action body) => WpfHost.Run(body);

    private static CheckSummaryCard Card(CatalogCheckResult result, bool offerConfirm = true, Action? confirm = null, Action? cancel = null)
    {
        var app = Application.Current!;
        if (app.Resources.MergedDictionaries.Count == 0)
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KidaGoSync;component/Theme.xaml", UriKind.Relative) });
        var card = new CheckSummaryCard();
        card.Show(result, "Corregir", offerConfirm, confirm ?? (() => { }), cancel ?? (() => { }));
        card.Measure(new Size(420, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        return card;
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock t) yield return t.Text;
        if (root is ContentControl { Content: string s }) yield return s;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var text in Texts(child)) yield return text;
    }

    private static Button Find(CheckSummaryCard card, string name) => (Button)card.FindName(name);

    [Fact]
    public void ShowsCountAndExamplesPerRuleThatApplied() => Sta(() =>
    {
        var result = CatalogChecker.Check(string.Concat(Enumerable.Repeat("12345678901234\n", 25)) + "012345678905\n");
        var texts = Texts(Card(result)).ToList();
        Assert.Contains(PanelText.RuleLabel(CatalogRule.TooLong), texts);
        Assert.Contains("25 líneas", texts);
        Assert.Equal(10, texts.Count(x => x.EndsWith(": 12345678901234")));
        Assert.Contains("1: 12345678901234", texts);
        Assert.Contains("1 línea", texts);
        Assert.Contains("26: 012345678905", texts);
        Assert.DoesNotContain(PanelText.CheckClean, texts);
    });

    [Fact]
    public void ACleanFileShowsCatalogoCorrectoAndOnlyACloseButton() => Sta(() =>
    {
        var card = Card(CatalogChecker.Check("8601153100055\n"));
        var texts = Texts(card).ToList();
        Assert.Contains("Catálogo correcto", texts);
        Assert.Contains("✓", texts);
        Assert.Equal(Visibility.Collapsed, Find(card, "ConfirmButton").Visibility);
        Assert.Equal(PanelText.CheckClose, Find(card, "CancelButton").Content);
    });

    [Fact]
    public void ButtonsCarryTheUsersChoiceAndCancelIsNeutralWhileConfirmIsTheAccent() => Sta(() =>
    {
        int confirmed = 0, cancelled = 0;
        var card = Card(CatalogChecker.Check("bad\n"), confirm: () => confirmed++, cancel: () => cancelled++);
        var confirm = Find(card, "ConfirmButton");
        var cancel = Find(card, "CancelButton");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal((1, 1), (confirmed, cancelled));
        Assert.Equal("Corregir", confirm.Content);
        Assert.Equal(PanelText.CheckCancel, cancel.Content);
        Assert.Equal(Color.FromRgb(0xE3, 0x00, 0x1B), ((SolidColorBrush)confirm.Background).Color); // the red confirm action
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), ((SolidColorBrush)cancel.Background).Color);  // neutral white
    });

    [Fact]
    public void WhenOnlyCancelIsOfferedTheConfirmButtonIsHidden() => Sta(() =>
    {
        var card = Card(CatalogChecker.Check("bad\n"), offerConfirm: false);
        Assert.Equal(Visibility.Collapsed, Find(card, "ConfirmButton").Visibility);
        Assert.Equal(PanelText.CheckCancel, Find(card, "CancelButton").Content);
    });

    /// <summary>With KIDAGO_RENDER_DIR set, writes the summary as a PNG so it can be looked at; otherwise does nothing.</summary>
    [Fact]
    public void RenderForInspection() => Sta(() =>
    {
        var dir = Environment.GetEnvironmentVariable("KIDAGO_RENDER_DIR");
        if (dir is null) return;
        var card = Card(CatalogChecker.Check("8601153100055\n012345678905\n4444\nX0016SA1Z7\n" + string.Concat(Enumerable.Repeat("12345678901234\n", 12))));
        var host = new Border { Background = (Brush)Application.Current.Resources["BackgroundBrush"], Padding = new Thickness(20), Width = 460, Child = card };
        host.Measure(new Size(460, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(Path.Combine(dir, "check-summary.png"));
        encoder.Save(stream);
    });
}
