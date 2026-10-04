using System.IO;
using System.Text;
using KidaGoSync.Connection;

namespace KidaGoSync.Sync;

/// <summary>
/// The data-movement half of Exportar, run once the user has confirmed (FEAT-005 TD, ListRetriever): pull the list off
/// the PDA, offer a save picker, write the local copy, show the text, and only then empty the PDA's file. The PDA's
/// list is never emptied before the local copy is written (C5, C13); a missing list file is a failure that clears
/// nothing (C14); cancelling the save aborts with nothing shown and the PDA file untouched (C12). The text is never
/// parsed or validated, only moved and displayed.
/// </summary>
public sealed class ListRetriever
{
    /// <summary>Where the save picker always opens.</summary>
    public const string SaveFolder = @"C:\afede\kidago";

    private readonly Func<PdaDevice?> _device;
    private readonly IPdaStorage _storage;
    private readonly Func<string> _location;
    private readonly Func<string?> _pickSavePath;
    private readonly Action<string> _showText;

    /// <param name="device">The PDA in use, or null when there is none.</param>
    /// <param name="location">The configured scanned-list location on the PDA (setting).</param>
    /// <param name="pickSavePath">Shows the save picker and returns the chosen path, or null when cancelled.</param>
    /// <param name="showText">Hands the pulled text to the read-only TXT view.</param>
    public ListRetriever(Func<PdaDevice?> device, IPdaStorage storage, Func<string> location, Func<string?> pickSavePath, Action<string> showText)
    {
        _device = device;
        _storage = storage;
        _location = location;
        _pickSavePath = pickSavePath;
        _showText = showText;
    }

    public async Task<ActionOutcome?> RetrieveAsync()
    {
        if (_device() is not { } pda) return new ActionOutcome(false, "No hay PDA conectada"); // lost between the confirmation and now
        var location = _location();

        byte[]? pulled;
        try
        {
            pulled = await _storage.ReadFileAsync(pda, location);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, e.Message);
        }
        if (pulled is null) return new ActionOutcome(false, $"No existe {location} en la PDA"); // C14: nothing cleared

        if (_pickSavePath() is not { } savePath) return null; // C12: cancelled, nothing shown, PDA untouched
        try
        {
            await File.WriteAllBytesAsync(savePath, pulled);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, e.Message); // C5: the local copy failed, so the PDA list stays
        }

        _showText(Encoding.UTF8.GetString(pulled)); // C11: viewing never gates the clearing

        try
        {
            await _storage.EmptyFileAsync(pda, location);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, $"Copia guardada, pero no se pudo vaciar la lista en la PDA: {e.Message}");
        }
        return new ActionOutcome(true);
    }
}
