using System.Windows;
using System.Windows.Media;

namespace KidaGoSync.Tests;

public class ThemeTests
{
    [Fact]
    public void Theme_tokens_match_project_visual_foundations()
    {
        _ = Application.Current ?? new Application(); // registers the pack:// scheme
        var theme = new ResourceDictionary { Source = new Uri("/KidaGoSync;component/Theme.xaml", UriKind.Relative) };

        Assert.Equal(Color.FromRgb(0xF7, 0xF6, 0xF3), (Color)theme["BackgroundColor"]);
        Assert.Equal(Color.FromRgb(0xE3, 0x00, 0x1B), (Color)theme["AccentColor"]);
        Assert.Equal(Color.FromRgb(0x1B, 0x88, 0x2B), (Color)theme["SuccessColor"]);
        Assert.Equal(Color.FromRgb(0xD9, 0xD9, 0xD9), (Color)theme["BorderColor"]);
    }
}

