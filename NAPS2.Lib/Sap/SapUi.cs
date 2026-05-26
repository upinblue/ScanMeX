using System.Globalization;

namespace NAPS2.Sap;

internal static class SapUi
{
    private static bool De => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase);

    public static string ArchiveLink => "SAP ArchiveLink";
    public static string EnableUpload => De ? "Upload zu SAP ArchiveLink aktivieren" : "Enable SAP ArchiveLink upload";
    public static string SapObjectType => De ? "SAP-Objekttyp" : "SAP object type";
    public static string ArchiveId => De ? "Archiv-ID" : "Archive ID";
    public static string DocumentType => De ? "Dokument-Typ" : "Document type";
    public static string ObjectKeySource => De ? "Objektschlüssel-Quelle" : "Object key source";
    public static string PromptEachScan => De ? "Bei jedem Scan abfragen" : "Prompt for every scan";
    public static string FromBarcode => De ? "Aus Barcode" : "From barcode";
    public static string FromFilename => De ? "Aus Dateiname" : "From filename";
    public static string FixedValue => De ? "Fester Wert" : "Fixed value";
    public static string Regex => "Regex";
    public static string Description => De ? "Beschreibung" : "Description";
    public static string DescriptionHint => De ? "Platzhalter: {date}, {user}, {objectkey}" : "Placeholders: {date}, {user}, {objectkey}";
    public static string TestConnection => De ? "Verbindung testen" : "Test connection";
    public static string SapConnection => De ? "SAP-Verbindung" : "SAP connection";
    public static string ConnectionOk => De ? "Verbindung erfolgreich getestet." : "Connection test succeeded.";
    public static string ConnectionFailed => De ? "Verbindungstest fehlgeschlagen: {0}" : "Connection test failed: {0}";
    public static string ObjectKeyPromptTitle => De ? "SAP-Objektschlüssel" : "SAP object key";
    public static string ObjectKeyPrompt => De ? "Objektschlüssel für SAP ArchiveLink eingeben:" : "Enter object key for SAP ArchiveLink:";
    public static string UploadTitle => De ? "Upload zu SAP ArchiveLink" : "Upload to SAP ArchiveLink";
    public static string UploadPreparing(string fileName) => De ? $"SAP-Upload für {fileName} vorbereiten" : $"Preparing SAP upload for {fileName}";
    public static string Uploading => De ? "Dokument nach SAP ArchiveLink hochladen" : "Uploading document to SAP ArchiveLink";
    public static string UploadSuccess(string docId) => De ? $"SAP ArchiveLink Upload erfolgreich. ARCHIV_DOC_ID: {docId}" : $"SAP ArchiveLink upload succeeded. ARCHIV_DOC_ID: {docId}";
    public static string UploadFailed(string message) => De ? $"SAP ArchiveLink Upload fehlgeschlagen: {message}" : $"SAP ArchiveLink upload failed: {message}";
    public static string ConfigureSapConnection => De ? "SAP-Verbindung konfigurieren" : "Configure SAP connection";
}
