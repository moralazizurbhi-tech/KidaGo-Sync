using System.IO;

namespace KidaGoSync.Sync;

/// <summary>A catalog file after the check: what it said, and the corrected copy that could be saved (FEAT-005 C16, C18).</summary>
public sealed record CatalogAnalysis(string FilePath, CatalogCheckResult Result, byte[] NormalizedBytes)
{
    /// <summary>The original dated name, which the save picker proposes.</summary>
    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>
/// The Comprobar catálogo flow around <see cref="CatalogChecker"/> (FEAT-005 TD): pick a file, analyze it, and on
/// "Corregir" save the corrected copy where the user chooses. The file is only read, so the original is never modified
/// (C18), and nothing is created before the user confirms (C16). Works without the PDA (C20).
/// </summary>
public sealed class CatalogCheckFlow
{
    /// <summary>Where both pickers open.</summary>
    public const string PickerFolder = CatalogSender.PickerFolder;

    private readonly Func<string?> _pickFile;
    private readonly Func<string, string?> _pickSavePath;

    /// <param name="pickFile">Shows the open picker at <see cref="PickerFolder"/>; the chosen path, or null when cancelled.</param>
    /// <param name="pickSavePath">Shows the save picker proposing the given file name; the chosen path, or null when cancelled.</param>
    public CatalogCheckFlow(Func<string?> pickFile, Func<string, string?> pickSavePath)
    {
        _pickFile = pickFile;
        _pickSavePath = pickSavePath;
    }

    /// <summary>Picks and analyzes a file. Null when the picker was cancelled; throws when the file cannot be read.</summary>
    public async Task<CatalogAnalysis?> AnalyzeAsync()
    {
        if (_pickFile() is not { } path) return null;
        var (result, bytes) = await Task.Run(() => CatalogChecker.CheckBytes(File.ReadAllBytes(path)));
        return new CatalogAnalysis(path, result, bytes);
    }

    /// <summary>
    /// "Corregir": saves the corrected lines. A failure when no valid line would remain (nothing is saved and the user is
    /// told); null when the save picker was cancelled (nothing saved, nothing to report).
    /// </summary>
    public async Task<ActionOutcome?> SaveCorrectedAsync(CatalogAnalysis analysis)
    {
        if (analysis.Result.HasNoValidLine) return new ActionOutcome(false, "No queda ninguna línea válida; no se ha guardado nada");
        if (_pickSavePath(analysis.FileName) is not { } target) return null;
        try
        {
            await File.WriteAllBytesAsync(target, analysis.NormalizedBytes);
            return new ActionOutcome(true, target);
        }
        catch (Exception e)
        {
            return new ActionOutcome(false, e.Message);
        }
    }
}
