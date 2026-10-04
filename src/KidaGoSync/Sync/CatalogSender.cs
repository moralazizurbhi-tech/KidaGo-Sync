using System.IO;
using KidaGoSync.Connection;

namespace KidaGoSync.Sync;

/// <summary>Writes files into the PDA's shared storage over MTP. The OS-level boundary, as with <see cref="IMtpDeviceSource"/>.</summary>
public interface IPdaStorage
{
    /// <summary>
    /// Copies the local file, byte for byte, to <c>&lt;PDA root&gt;/&lt;folder&gt;/&lt;fileName&gt;</c>, creating the folder
    /// when it is missing and replacing a file of the same name.
    /// </summary>
    Task SendFileAsync(PdaDevice device, string localPath, string folder, string fileName);

    /// <summary>Reads a file by its path under the PDA root (forward slashes); null when it does not exist.</summary>
    Task<byte[]?> ReadFileAsync(PdaDevice device, string path);

    /// <summary>Leaves the file at that path existing but with no content. Never deletes it.</summary>
    Task EmptyFileAsync(PdaDevice device, string path);
}

/// <summary>
/// Importar: sends a catalog file the user picks to the PDA's catalog folder (FEAT-005 TD, CatalogSender). The file is
/// checked first (C19). A clean file is copied untouched under its dated name (C3). A file with problems is never sent
/// as it is: only the corrected lines, under the original name, when the user chooses to normalize; the original on the
/// computer is never modified. Cancelling the picker or the summary is not an outcome.
/// </summary>
public sealed class CatalogSender
{
    /// <summary>Where the picker always opens.</summary>
    public const string PickerFolder = @"C:\afede\kidago";

    /// <summary>The fixed catalog folder on the PDA, relative to its shared-storage root.</summary>
    public const string PdaFolder = "afede/kidago";

    private readonly Func<string?> _pickFile;
    private readonly Func<PdaDevice?> _device;
    private readonly IPdaStorage _storage;

    /// <param name="pickFile">Shows the picker and returns the chosen path, or null when cancelled.</param>
    /// <param name="device">The PDA in use, or null when there is none.</param>
    public CatalogSender(Func<string?> pickFile, Func<PdaDevice?> device, IPdaStorage storage)
    {
        _pickFile = pickFile;
        _device = device;
        _storage = storage;
    }

    /// <param name="confirmNormalize">
    /// Shows the check summary of a file with problems and answers whether to send the corrected lines ("Normalizar e
    /// importar") or nothing ("Cancelar"). Never asked for a clean file; when no valid line would remain, the answer is
    /// ignored and nothing is sent (C19).
    /// </param>
    public async Task<ActionOutcome?> SendAsync(Func<CatalogAnalysis, Task<bool>> confirmNormalize)
    {
        if (_device() is not { } pda) return new ActionOutcome(false, "No hay PDA conectada"); // lost between the click and now
        if (_pickFile() is not { } path) return null;
        string? temp = null;
        try
        {
            var (result, bytes) = await Task.Run(() => CatalogChecker.CheckBytes(File.ReadAllBytes(path)));
            var name = Path.GetFileName(path);
            var toSend = path;
            if (!result.IsClean)
            {
                var analysis = new CatalogAnalysis(path, result, bytes);
                if (!await confirmNormalize(analysis) || result.HasNoValidLine) return null;
                if (_device() is not { } still) return new ActionOutcome(false, "No hay PDA conectada"); // lost while the summary was open
                pda = still;
                temp = Path.Combine(Path.GetTempPath(), $"kidago-{Guid.NewGuid():N}.txt");
                await File.WriteAllBytesAsync(temp, bytes);
                toSend = temp;
            }
            await _storage.SendFileAsync(pda, toSend, PdaFolder, name);
            return new ActionOutcome(true);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, e.Message);
        }
        finally
        {
            if (temp is not null) File.Delete(temp);
        }
    }
}
