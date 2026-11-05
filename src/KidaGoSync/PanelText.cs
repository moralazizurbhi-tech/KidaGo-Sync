using KidaGoSync.Sync;

namespace KidaGoSync;

/// <summary>
/// Panel copy. The Feature UX Specification lists every one of these strings as an Unresolved Asset Decision
/// ("Feature UI to define"), so all of it is placeholder Spanish, kept in one place to be replaced once defined.
/// </summary>
public static class PanelText
{
    public const string Connected = "PDA conectada";
    public const string Disconnected = "PDA desconectada";
    public const string ChoosingDevice = "Elige cuál es la PDA";
    public const string DeviceChooserLabel = "Dispositivo";

    public const string Importar = "Actualizar PDA";
    public const string Exportar = "Descargar artículos escaneados";
    public const string Working = "En curso…";

    public const string ImportarSuccess = "PDA actualizada";
    public const string ImportarFailure = "No se pudo actualizar la PDA";
    public const string ExportarSuccess = "Artículos descargados";
    public const string ExportarFailure = "No se pudieron descargar los artículos";

    public const string ConfirmExportTitle = "¿Descargar los artículos escaneados?";
    public const string ConfirmExportBody = "Se descargarán los artículos escaneados y se vaciará la lista en la PDA.";
    public const string ConfirmExportConfirm = "Descargar";
    public const string ConfirmExportDecline = "Cancelar";

    public const string Comprobar = "Comprobar catálogo";
    public const string ComprobarSuccess = "Copia corregida guardada";
    public const string ComprobarFailure = "No se pudo guardar la copia corregida";

    // Catalog check summary dialog (FEAT-005 C21). Draft Spanish copy: the Feature UX lists the rule labels as an Unresolved Asset Decision.
    public const string CheckTitle = "Resumen del catálogo";
    public const string CheckClean = "Catálogo correcto";
    public const string CheckEmpty = "El archivo no contiene ninguna línea válida";
    public const string CheckNormalizeImport = "Normalizar y actualizar PDA";
    public const string CheckCorrect = "Corregir";
    public const string CheckCancel = "Cancelar";
    public const string CheckClose = "Cerrar";

    /// <summary>What a rule did to the lines it applied to.</summary>
    public static string RuleLabel(CatalogRule rule) => rule switch
    {
        CatalogRule.Padded => "12 dígitos: se añade un cero al principio",
        CatalogRule.Trimmed => "Espacios sobrantes: se limpian",
        CatalogRule.Blank => "Líneas en blanco: se quitan",
        CatalogRule.NonDigit => "Contienen caracteres que no son dígitos: se quitan",
        CatalogRule.TooLong => "Más de 13 dígitos: se quitan",
        _ => rule.ToString(),
    };

    public static string RuleCount(int count) => count == 1 ? "1 línea" : $"{count} líneas";

    /// <summary>An example as the summary lists it: the line number, then the value.</summary>
    public static string ExampleLine(LineExample example) => $"{example.LineNumber}: {example.Value}";

    public const string PathLabel = "Ubicación de la lista en la PDA";
    public const string TxtViewLabel = "Artículos escaneados";
    public const string EmptyListMessage = "La lista estaba vacía";

    /// <summary>What the read-only TXT view shows for a pulled file: its raw text, or the empty-list message when it has no lines (C11).</summary>
    public static string TxtViewContent(string rawText) => string.IsNullOrWhiteSpace(rawText) ? EmptyListMessage : rawText;

    public const string NotAvailableYet = "Aún no disponible";
}
