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

    public const string Importar = "Importar";
    public const string Exportar = "Exportar";
    public const string Working = "En curso…";

    public const string ImportarSuccess = "Catálogo enviado";
    public const string ImportarFailure = "No se pudo enviar el catálogo";
    public const string ExportarSuccess = "Lista exportada";
    public const string ExportarFailure = "No se pudo exportar la lista";

    public const string ConfirmExportTitle = "¿Exportar la lista?";
    public const string ConfirmExportBody = "Se descargará la lista y se vaciará en la PDA.";
    public const string ConfirmExportConfirm = "Exportar";
    public const string ConfirmExportDecline = "Cancelar";

    public const string PathLabel = "Ubicación de la lista en la PDA";
    public const string TxtViewLabel = "Lista exportada";
    public const string EmptyListMessage = "La lista estaba vacía";

    /// <summary>What the read-only TXT view shows for a pulled file: its raw text, or the empty-list message when it has no lines (C11).</summary>
    public static string TxtViewContent(string rawText) => string.IsNullOrWhiteSpace(rawText) ? EmptyListMessage : rawText;

    public const string NotAvailableYet = "Aún no disponible";
}
