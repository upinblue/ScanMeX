using Microsoft.Extensions.Logging;

namespace NAPS2.Logging;

/// <summary>
/// The channel for everything the operator needs to see in the console window: what the scanner did,
/// what the barcode detection found (or didn't), how a scan was split into documents, and what happened
/// on the way to SharePoint and SAP.
///
/// IMPORTANT FOR ANYONE CHANGING THE SCAN, BARCODE, SEPARATION OR UPLOAD CODE:
/// every step that can succeed, fail, or silently do nothing has to report itself here. A step that
/// quietly does nothing is exactly the case the console exists for -- "no barcode found", "upload target
/// not enabled", "profile has no auto save path" are as important as the success messages.
///
/// These go out at Debug level, so they only reach debuglog.txt when debug logging is switched on, but
/// they always reach the console window.
/// </summary>
public static class ScanConsole
{
    /// <summary>Scanner and page acquisition: device, driver, settings, each page, cancellation.</summary>
    public static void Scan(string message) => Write("Scan", message);

    /// <summary>Barcode detection results, including pages where nothing was found.</summary>
    public static void Barcode(string message) => Write("Barcode", message);

    /// <summary>Splitting a scan into documents, identification values, and file naming.</summary>
    public static void Document(string message) => Write("Document", message);

    /// <summary>Everything on the way to SharePoint and SAP ArchiveLink.</summary>
    public static void Upload(string message) => Write("Upload", message);

    /// <summary>The profile settings a scan is running with, so silent no-ops can be explained.</summary>
    public static void Profile(string message) => Write("Profile", message);

    /// <summary>Application-level context such as version and startup.</summary>
    public static void App(string message) => Write("App", message);

    private static void Write(string category, string message) =>
        Log.Logger.LogDebug("[{Category}] {Message}", category, message);
}
