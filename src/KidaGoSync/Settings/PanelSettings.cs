using System.IO;
using System.Text.Json;

namespace KidaGoSync.Settings;

/// <summary>
/// KidaGo-Sync's local settings (FEAT-005 TD, ListRetriever's "scanned-list location"). One value for now: where on the
/// PDA Exportar reads the list from. Every accepted change is written to disk before <see cref="TrySetScannedListLocation"/>
/// returns, so it survives a restart (C10). There is no catalog-destination setting.
/// </summary>
public sealed class PanelSettings
{
    /// <summary>Where the mobile app writes the list, relative to the PDA's shared-storage root.</summary>
    public const string DefaultScannedListLocation = "afede/kidago/scanned-list.txt";

    private sealed record Stored(string? ScannedListLocation);

    private readonly string _path;

    public PanelSettings(string path)
    {
        _path = path;
        ScannedListLocation = Load() ?? DefaultScannedListLocation;
    }

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KidaGoSync", "settings.json");

    public string ScannedListLocation { get; private set; }

    /// <summary>Saves a non-empty location and returns true; an empty or blank one is rejected and the previous value stays.</summary>
    public bool TrySetScannedListLocation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(value)));
        ScannedListLocation = value;
        return true;
    }

    private string? Load()
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            return string.IsNullOrWhiteSpace(stored?.ScannedListLocation) ? null : stored.ScannedListLocation;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // missing or unreadable: the default applies
        }
    }
}
