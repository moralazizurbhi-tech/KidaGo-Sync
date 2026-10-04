using System.IO;
using KidaGoSync.Settings;

namespace KidaGoSync.Tests;

public class PanelSettingsTests
{
    private readonly string _file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "sub", "settings.json");

    [Fact]
    public void DefaultsToTheFolderWhereTheMobileAppWritesTheList()
    {
        Assert.Equal("afede/kidago/scanned-list.txt", new PanelSettings(_file).ScannedListLocation);
    }

    [Fact]
    public void AChangeIsStillInEffectAfterARestart()
    {
        Assert.True(new PanelSettings(_file).TrySetScannedListLocation("otra/carpeta/lista.txt"));
        Assert.Equal("otra/carpeta/lista.txt", new PanelSettings(_file).ScannedListLocation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyValueIsRejectedAndThePreviousOneStays(string empty)
    {
        var settings = new PanelSettings(_file);
        settings.TrySetScannedListLocation("a/b.txt");
        Assert.False(settings.TrySetScannedListLocation(empty));
        Assert.Equal("a/b.txt", settings.ScannedListLocation);
        Assert.Equal("a/b.txt", new PanelSettings(_file).ScannedListLocation);
    }

    [Fact]
    public void SurroundingSpacesAreTrimmed()
    {
        var settings = new PanelSettings(_file);
        settings.TrySetScannedListLocation("  a/b.txt ");
        Assert.Equal("a/b.txt", settings.ScannedListLocation);
    }

    [Fact]
    public void AnUnreadableFileFallsBackToTheDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ not json");
        Assert.Equal(PanelSettings.DefaultScannedListLocation, new PanelSettings(_file).ScannedListLocation);
    }
}

public class TxtViewContentTests
{
    [Fact]
    public void ANonEmptyListIsShownAsItsRawText() => Assert.Equal("A,1\nB,2\n", PanelText.TxtViewContent("A,1\nB,2\n"));

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void AnEmptyListShowsTheEmptyMessageNotAnError(string raw) => Assert.Equal(PanelText.EmptyListMessage, PanelText.TxtViewContent(raw));
}
