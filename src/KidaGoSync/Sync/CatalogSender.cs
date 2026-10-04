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
/// copied untouched and keeps its dated name; whether its content is a valid catalog is the mobile app's concern, so
/// nothing is read or checked here (C3). Cancelling the picker is not an outcome.
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

    public async Task<ActionOutcome?> SendAsync()
    {
        if (_device() is not { } pda) return new ActionOutcome(false, "No hay PDA conectada"); // lost between the click and now
        if (_pickFile() is not { } path) return null;
        try
        {
            await _storage.SendFileAsync(pda, path, PdaFolder, Path.GetFileName(path));
            return new ActionOutcome(true);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, e.Message);
        }
    }
}
