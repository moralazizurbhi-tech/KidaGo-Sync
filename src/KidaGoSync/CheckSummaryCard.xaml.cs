using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KidaGoSync.Sync;

namespace KidaGoSync;

/// <summary>
/// The catalog check summary (FEAT-005 C21): per rule that applied, its label, the number of lines and up to ten examples
/// (line number and value); or "Catálogo correcto" with a single close button when nothing applied. It only presents:
/// the two callbacks carry the user's choice to the coordinator.
/// </summary>
public partial class CheckSummaryCard : UserControl
{
    private Action _confirm = () => { };
    private Action _cancel = () => { };

    public CheckSummaryCard() => InitializeComponent();

    /// <param name="confirmLabel">"Corregir" for Comprobar, "Normalizar e importar" for Importar (C19).</param>
    /// <param name="offerConfirm">False when nothing valid would remain and the only choice is to cancel (Importar, C19).</param>
    public void Show(CatalogCheckResult result, string confirmLabel, bool offerConfirm, Action confirm, Action cancel)
    {
        _confirm = confirm;
        _cancel = cancel;
        TitleText.Text = PanelText.CheckTitle;
        Body.Children.Clear();
        if (result.IsClean)
        {
            Body.Children.Add(Row(PanelText.CheckClean, null, "✓", "SuccessBrush", 16));
            ConfirmButton.Visibility = VisibleWhen(false);
            CancelButton.Content = PanelText.CheckClose;
        }
        else
        {
            if (result.Rules.Count == 0) Body.Children.Add(Row(PanelText.CheckEmpty, null, "!", "AccentBrush", 14)); // no line at all
            foreach (var rule in result.Rules) Body.Children.Add(RuleBlock(rule));
            ConfirmButton.Content = confirmLabel;
            ConfirmButton.Visibility = VisibleWhen(offerConfirm);
            CancelButton.Content = PanelText.CheckCancel;
        }
        Grid.SetColumn(CancelButton, ConfirmButton.Visibility == Visibility.Visible ? 2 : 0);
        Grid.SetColumnSpan(CancelButton, ConfirmButton.Visibility == Visibility.Visible ? 1 : 3);
    }

    private Visibility VisibleWhen(bool confirmVisible) => confirmVisible ? Visibility.Visible : Visibility.Collapsed;

    private UIElement RuleBlock(RuleSummary rule)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        block.Children.Add(Row(PanelText.RuleLabel(rule.Rule), PanelText.RuleCount(rule.Count), "!", "AccentBrush", 14));
        foreach (var example in rule.Examples)
        {
            block.Children.Add(new TextBlock
            {
                Text = PanelText.ExampleLine(example),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(34, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        return block;
    }

    /// <summary>A glyph badge (a shape as well as a colour) followed by the label and, when given, the count.</summary>
    private UIElement Row(string label, string? count, string glyph, string brushKey, double glyphSize)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)FindResource(brushKey),
            Child = new TextBlock { Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = glyphSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 });
        if (count is not null) row.Children.Add(new TextBlock { Text = count, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => _confirm();

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cancel();
}
