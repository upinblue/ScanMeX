using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Xml.Serialization;
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Serialization;

namespace NAPS2.Scan;

/// <summary>
/// A class that stores user configuration for scanning, including device selection and other options.
/// </summary>
public class ScanProfile
{
    public const int CURRENT_VERSION = 2;

    public ScanProfile()
    {
        // Set defaults
        BitDepth = ScanBitDepth.C24Bit;
        PageAlign = ScanHorizontalAlign.Right;
        PageSize = ScanPageSize.Letter;
        Resolution.Dpi = 200;
        PaperSource = ScanSource.Glass;
        Quality = 75;
        BlankPageWhiteThreshold = 70;
        BlankPageCoverageThreshold = 15;
        WiaDelayBetweenScansSeconds = 2.0;
        EnableSharePointUpload = false;
        SharePointUploadSettings = new SharePointUploadSettings();
    }

    public ScanProfile Clone()
    {
        // Easy deep copy. Ideally we'd do this in a more efficient way.
        var copy = this.ToXml().FromXml<ScanProfile>();
        // Copy XmlIgnore properties
        copy.UpgradedFrom = UpgradedFrom;
        copy.IsLocked = IsLocked;
        copy.IsDeviceLocked = IsDeviceLocked;
        return copy;
    }

    public override string ToString() => DisplayName;

    public int? Version { get; set; }

    [XmlIgnore]
    public int? UpgradedFrom { get; set; }

    // TODO: These shouldn't be part of this class
    [XmlIgnore]
    public bool IsLocked { get; set; }

    [XmlIgnore]
    public bool IsDeviceLocked { get; set; }

    public ScanProfileDevice? Device { get; set; }

    public ScanProfileCaps? Caps { get; set; }

    public string? DriverName { get; set; }

    public string DisplayName { get; set; } = "";

    public int IconID { get; set; }

    public bool MaxQuality { get; set; }

    public bool IsDefault { get; set; }

    public bool UseNativeUI { get; set; }

    public ScanScale AfterScanScale { get; set; }

    public int Brightness { get; set; }

    public int Contrast { get; set; }

    public ScanBitDepth BitDepth { get; set; }

    public ScanHorizontalAlign PageAlign { get; set; }

    public ScanPageSize PageSize { get; set; }

    public string? CustomPageSizeName { get; set; }

    public PageDimensions? CustomPageSize { get; set; }

    public ScanResolution Resolution { get; set; } = new();

    public ScanSource PaperSource { get; set; }

    public bool EnableAutoSave { get; set; }

    public AutoSaveSettings? AutoSaveSettings { get; set; }

    /// <summary>
    /// Enables full barcode recognition for placeholder and upload processing. Disabled by default to avoid performance impact for existing profiles.
    /// </summary>
    public bool BarcodeRecognitionEnabled { get; set; }

    public int Quality { get; set; }

    public bool AutoDeskew { get; set; }

    public double RotateDegrees { get; set; }

    public bool BrightnessContrastAfterScan { get; set; }

    public bool ForcePageSize { get; set; }

    public bool ForcePageSizeCrop { get; set; }

    public TwainImpl TwainImpl { get; set; }

    public bool TwainProgress { get; set; }

    public bool ExcludeBlankPages { get; set; }

    public int BlankPageWhiteThreshold { get; set; }

    public int BlankPageCoverageThreshold { get; set; }

    public bool WiaOffsetWidth { get; set; }

    public bool WiaRetryOnFailure { get; set; }

    public bool WiaDelayBetweenScans { get; set; }

    public double WiaDelayBetweenScansSeconds { get; set; }

    public WiaApiVersion WiaVersion { get; set; }

    public bool FlipDuplexedPages { get; set; }

    public KeyValueScanOptions? KeyValueOptions { get; set; }

    /// <summary>
    /// Enables uploading the generated PDF(s) for this profile to SharePoint Online via Microsoft Graph (app-only).
    /// </summary>
    public bool EnableSharePointUpload { get; set; }

    /// <summary>
    /// Settings for SharePoint Online upload when <see cref="EnableSharePointUpload"/> is true.
    /// </summary>
    public SharePointUploadSettings SharePointUploadSettings { get; set; }

    /// <summary>
    /// Optional SAP ArchiveLink settings. Null keeps XML profile serialization backward-compatible for existing profiles.
    /// </summary>
    [XmlElement(IsNullable = true)]
    public SapArchiveProfileSettings? SapArchiveSettings { get; set; }

    /// <summary>
    /// Optional document separation, identification and upload settings. Null means the settings are
    /// derived from <see cref="AutoSaveSettings"/>, which keeps profiles saved before this backward-compatible.
    /// Use <see cref="DocumentWorkflowSettings.ForProfile"/> instead of reading this directly.
    /// </summary>
    [XmlElement(IsNullable = true)]
    public DocumentWorkflowSettings? DocumentWorkflow { get; set; }

    /// <summary>
    /// Whether scanned documents go to SharePoint. Enabling this historically lived in two places --
    /// <see cref="EnableSharePointUpload"/> on the profile and <see cref="AutoSaveSettings.UploadToSharePoint"/>
    /// on the auto save settings -- which disagreed depending on which dialog was used. Both are now written
    /// together, and either one counts so profiles saved by older builds keep working.
    /// </summary>
    public bool UploadsToSharePoint() =>
        (EnableSharePointUpload || AutoSaveSettings?.UploadToSharePoint == true) &&
        SharePointUploadSettings != null;

    /// <summary>
    /// Whether scanned documents go to SAP ArchiveLink. See <see cref="UploadsToSharePoint"/> for why
    /// two flags are consulted.
    /// </summary>
    public bool UploadsToSap() =>
        SapArchiveSettings != null &&
        (SapArchiveSettings.EnableUpload || AutoSaveSettings?.UploadToSap == true);

    /// <summary>
    /// The regex that decides which of a page's barcodes is the one the operator means, or null when the
    /// profile doesn't distinguish them.
    /// </summary>
    /// <remarks>
    /// One pattern, used everywhere. It names the document's file, decides which barcode the variables
    /// yield, and -- through the document's identification -- supplies the SAP object key, so all of
    /// those necessarily agree. There was a second regex on the SAP settings; a profile could then select
    /// one barcode for its file name and another for the archive, and nothing afterwards showed that the
    /// two had parted company. <see cref="DocumentWorkflowSettings.ForProfile"/> folds the old SAP value
    /// in for profiles that only ever set that one.
    /// </remarks>
    public string? GetBarcodeSelectionPattern() =>
        DocumentWorkflowSettings.ForProfile(this).SeparationPattern;

    /// <summary>
    /// Whether anything on this profile needs the pages' barcodes decoded.
    /// </summary>
    /// <remarks>
    /// Separation and the SAP "from the scanned barcode" object key are the obvious cases. The one that
    /// is easy to miss is a template: an auto save path of <c>Scans\$(barcode).pdf</c>, a SharePoint
    /// folder of <c>$(barcode)</c> or a SAP object id of <c>$(barcode)</c> needs the pages decoded just
    /// as much. Without detection the placeholder resolves to nothing, and the document is filed under a
    /// name with a hole in it rather than failing visibly -- so every template that goes through
    /// <see cref="ImportExport.FileNamePlaceholders"/> has to be listed here.
    /// </remarks>
    public bool NeedsBarcodeValues()
    {
        if (DocumentWorkflowSettings.ForProfile(this).RequiresBarcodeDetection())
        {
            return true;
        }
        if (UploadsToSap() && SapArchiveSettings!.BarcodeSource == BarcodeSource.FromScannedBarcode)
        {
            return true;
        }
        return BarcodeRecognitionEnabled || UsesBarcodePlaceholder();
    }

    /// <summary>
    /// Whether any template on this profile expands to a barcode. <c>$(id)</c> counts because it falls
    /// back to the barcode whenever the operator isn't asked to type an identification number.
    /// </summary>
    private bool UsesBarcodePlaceholder()
    {
        var workflow = DocumentWorkflowSettings.ForProfile(this);
        var templates = new[]
        {
            workflow.LocalFolder,
            workflow.DocumentNameTemplate,
            AutoSaveSettings?.FilePath,
            SharePointUploadSettings?.SiteUrl,
            SharePointUploadSettings?.LibraryNameOrPath,
            SharePointUploadSettings?.FolderPath,
            SapArchiveSettings?.ObjectId,
            SapArchiveSettings?.SlugTemplate,
            SapArchiveSettings?.BarcodeTemplate,
            SapArchiveSettings?.DescriptionTemplate,
            SapArchiveSettings?.FixedBarcode,
            SapArchiveSettings?.BarcodeRegex
        };
        var wantsId = workflow.IdMode != DocumentIdMode.ManualInput;
        return templates.Any(x => ContainsPlaceholder(x, "$(barcode") ||
                                  wantsId && ContainsPlaceholder(x, "$(id)"));
    }

    private static bool ContainsPlaceholder(string? template, string placeholder) =>
        template?.IndexOf(placeholder, StringComparison.OrdinalIgnoreCase) >= 0;
}

/// <summary>
/// User configuration for the Auto Save feature, which saves to a file immediately after scanning.
/// </summary>
public record AutoSaveSettings
{
    public string FilePath { get; init; } = "";
    public bool PromptForFilePath { get; init; }
    public bool ClearImagesAfterSaving { get; init; }
    public SaveSeparator Separator { get; init; } = SaveSeparator.FilePerPage;
    // New: Optional regex for Code 39 barcode-based separation (used when Separator == SaveSeparator.Code39Barcode)
    public string? Code39SeparationPattern { get; init; }
    // New: When true, also upload the auto-saved PDF to SharePoint using the same filename.
    // Default is false for backward compatibility.
    public bool UploadToSharePoint { get; init; }
    public bool UploadToSap { get; init; }
}

/// <summary>
/// Configuration for SharePoint Online upload using Microsoft Graph client credentials.
/// All fields are optional; if omitted, uploading will be skipped. The Azure AD (Entra ID)
/// application must have appropriate Microsoft Graph application permissions (e.g. Sites.ReadWrite.All
/// or Sites.Selected) granted by an administrator.
/// </summary>
public record SharePointUploadSettings
{
    /// <summary>Full https:// URL of the SharePoint site (e.g. https://tenant.sharepoint.com/sites/Invoices).</summary>
    public string? SiteUrl { get; init; }
    /// <summary>Document library display name or relative URL (e.g. "Shared Documents" or "Shared Documents/Invoices").</summary>
    public string? LibraryNameOrPath { get; init; }
    /// <summary>Optional folder path inside the library (e.g. "2025/12").</summary>
    public string? FolderPath { get; init; }
    /// <summary>Azure AD Tenant ID (GUID).</summary>
    public string? TenantId { get; init; }
    /// <summary>Azure AD Application (Client) ID.</summary>
    public string? ClientId { get; init; }
    /// <summary>Client secret for the App Registration (plain text for now; should be secured later).</summary>
    public string? ClientSecret { get; init; }
}

/// <summary>
/// The type of TWAIN driver implementation (this option is provided for compatibility).
/// </summary>
public enum TwainImpl
{
    // The default is currently equivalent ot MemXfer
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_Default")]
    Default,
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_NativeXfer")]
    NativeXfer,
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_MemXfer")]
    MemXfer,
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_OldDsm")]
    OldDsm,
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_Legacy")]
    Legacy,
    [LocalizedDescription(typeof(SettingsResources), "TwainImpl_X64")]
    X64
}

/// <summary>
/// The physical source of the scanned image (flatbed, feeder).
/// </summary>
public enum ScanSource
{
    [LocalizedDescription(typeof(SettingsResources), "Source_Glass")]
    Glass,
    [LocalizedDescription(typeof(SettingsResources), "Source_Feeder")]
    Feeder,
    [LocalizedDescription(typeof(SettingsResources), "Source_Duplex")]
    Duplex
}

/// <summary>
/// The color depth used for scanning.
/// </summary>
public enum ScanBitDepth
{
    [LocalizedDescription(typeof(SettingsResources), "BitDepth_24Color")]
    C24Bit,
    [LocalizedDescription(typeof(SettingsResources), "BitDepth_8Grayscale")]
    Grayscale,
    [LocalizedDescription(typeof(SettingsResources), "BitDepth_1BlackAndWhite")]
    BlackWhite
}

/// <summary>
/// The resolution used for scanning.
/// </summary>
public enum ScanDpi
{
    Dpi100,
    Dpi150,
    Dpi200,
    Dpi300,
    Dpi400,
    Dpi600,
    Dpi800,
    Dpi1200,
    Dpi2400,
    Dpi4800
}

/// <summary>
/// The physical location of the page relative to the scan area.
/// </summary>
public enum ScanHorizontalAlign
{
    [LocalizedDescription(typeof(SettingsResources), "HorizontalAlign_Left")]
    Left,
    [LocalizedDescription(typeof(SettingsResources), "HorizontalAlign_Center")]
    Center,
    [LocalizedDescription(typeof(SettingsResources), "HorizontalAlign_Right")]
    Right
}

/// <summary>
/// A scale factor used to shrink the scanned image.
/// </summary>
public enum ScanScale
{
    [LocalizedDescription(typeof(SettingsResources), "Scale_1_1")]
    OneToOne,
    [LocalizedDescription(typeof(SettingsResources), "Scale_1_2")]
    OneToTwo,
    [LocalizedDescription(typeof(SettingsResources), "Scale_1_4")]
    OneToFour,
    [LocalizedDescription(typeof(SettingsResources), "Scale_1_8")]
    OneToEight
}

/// <summary>
/// The page size used for scanning.
/// </summary>
public enum ScanPageSize
{
    [LocalizedDescription(typeof(SettingsResources), "PageSize_Letter")]
    [PageDimensions("8.5", "11", LocalizedPageSizeUnit.Inch)]
    Letter,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_Legal")]
    [PageDimensions("8.5", "14", LocalizedPageSizeUnit.Inch)]
    Legal,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_A5")]
    [PageDimensions("148", "210", LocalizedPageSizeUnit.Millimetre)]
    A5,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_A4")]
    [PageDimensions("210", "297", LocalizedPageSizeUnit.Millimetre)]
    A4,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_A3")]
    [PageDimensions("297", "420", LocalizedPageSizeUnit.Millimetre)]
    A3,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_B5")]
    [PageDimensions("176", "250", LocalizedPageSizeUnit.Millimetre)]
    B5,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_B4")]
    [PageDimensions("250", "353", LocalizedPageSizeUnit.Millimetre)]
    B4,
    [LocalizedDescription(typeof(SettingsResources), "PageSize_Custom")]
    Custom
}

/// <summary>
/// Configuration for a particular page size.
/// </summary>
public record PageDimensions
{
    public decimal Width { get; init; }
    public decimal Height { get; init; }
    public LocalizedPageSizeUnit Unit { get; init; }
}

/// <summary>
/// Configuration for a user-created custom page size.
/// </summary>
public record NamedPageSize
{
    public string Name { get; init; } = "";
    public PageDimensions Dimens { get; init; } = new();
}

/// <summary>
/// Helper attribute used to assign physical dimensions to the ScanPageSize enum.
/// </summary>
public class PageDimensionsAttribute : Attribute
{
    public PageDimensionsAttribute(string width, string height, LocalizedPageSizeUnit unit)
    {
        PageDimensions = new PageDimensions
        {
            Width = decimal.Parse(width, CultureInfo.InvariantCulture),
            Height = decimal.Parse(height, CultureInfo.InvariantCulture),
            Unit = unit
        };
    }

    public PageDimensions PageDimensions { get; }
}

/// <summary>
/// The unit used for Width and Height in PageDimensions.
/// </summary>
public enum LocalizedPageSizeUnit
{
    [LocalizedDescription(typeof(SettingsResources), "PageSizeUnit_Inch")]
    Inch,
    [LocalizedDescription(typeof(SettingsResources), "PageSizeUnit_Centimetre")]
    Centimetre,
    [LocalizedDescription(typeof(SettingsResources), "PageSizeUnit_Millimetre")]
    Millimetre
}

/// <summary>
/// Helper extensions that get additional information from scan-related objects and enumerations.
/// </summary>
public static class ScanEnumExtensions
{
    public static PageDimensions? PageDimensions(this Enum enumValue)
    {
        var attrs = enumValue.GetType().GetField(enumValue.ToString())!.GetCustomAttributes<PageDimensionsAttribute>();
        return attrs.Select(x => x.PageDimensions).SingleOrDefault();
    }

    public static PageSize ToPageSize(this PageDimensions pageDimensions)
    {
        return new PageSize(pageDimensions.Width, pageDimensions.Height, (PageSizeUnit) pageDimensions.Unit);
    }

    public static int ToIntDpi(this ScanDpi enumValue)
    {
        switch (enumValue)
        {
            case ScanDpi.Dpi100:
                return 100;
            case ScanDpi.Dpi150:
                return 150;
            case ScanDpi.Dpi200:
                return 200;
            case ScanDpi.Dpi300:
                return 300;
            case ScanDpi.Dpi400:
                return 400;
            case ScanDpi.Dpi600:
                return 600;
            case ScanDpi.Dpi800:
                return 800;
            case ScanDpi.Dpi1200:
                return 1200;
            case ScanDpi.Dpi2400:
                return 2400;
            case ScanDpi.Dpi4800:
                return 4800;
            default:
                throw new ArgumentException();
        }
    }

    public static int ToIntScaleFactor(this ScanScale enumValue)
    {
        switch (enumValue)
        {
            case ScanScale.OneToOne:
                return 1;
            case ScanScale.OneToTwo:
                return 2;
            case ScanScale.OneToFour:
                return 4;
            case ScanScale.OneToEight:
                return 8;
            default:
                throw new ArgumentException();
        }
    }

    public static string Description(this Enum enumValue)
    {
        object[] attrs =
            enumValue.GetType().GetField(enumValue.ToString())!.GetCustomAttributes(typeof(DescriptionAttribute),
                false);
        return attrs.Cast<DescriptionAttribute>().Select(x => x.Description).SingleOrDefault() ?? "";
    }

    public static BitDepth ToBitDepth(this ScanBitDepth bitDepth)
    {
        switch (bitDepth)
        {
            case ScanBitDepth.C24Bit:
                return BitDepth.Color;
            case ScanBitDepth.Grayscale:
                return BitDepth.Grayscale;
            case ScanBitDepth.BlackWhite:
                return BitDepth.BlackAndWhite;
            default:
                throw new ArgumentException();
        }
    }

    public static ScanBitDepth ToScanBitDepth(this BitDepth bitDepth)
    {
        switch (bitDepth)
        {
            case BitDepth.Color:
                return ScanBitDepth.C24Bit;
            case BitDepth.Grayscale:
                return ScanBitDepth.Grayscale;
            case BitDepth.BlackAndWhite:
                return ScanBitDepth.BlackWhite;
            default:
                throw new ArgumentException();
        }
    }

    public static HorizontalAlign ToHorizontalAlign(this ScanHorizontalAlign horizontalAlign)
    {
        switch (horizontalAlign)
        {
            case ScanHorizontalAlign.Left:
                return HorizontalAlign.Left;
            case ScanHorizontalAlign.Right:
                return HorizontalAlign.Right;
            case ScanHorizontalAlign.Center:
                return HorizontalAlign.Center;
            default:
                throw new ArgumentException();
        }
    }

    public static PaperSource ToPaperSource(this ScanSource scanSource)
    {
        switch (scanSource)
        {
            case ScanSource.Glass:
                return PaperSource.Flatbed;
            case ScanSource.Feeder:
                return PaperSource.Feeder;
            case ScanSource.Duplex:
                return PaperSource.Duplex;
            default:
                throw new ArgumentException();
        }
    }
}