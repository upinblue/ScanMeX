using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NAPS2.Sap;

/// <summary>
/// Specifies where the SAP ArchiveLink barcode value should be obtained from.
/// </summary>
public enum BarcodeSource
{
    /// <summary>
    /// Use a fixed configured barcode value.
    /// </summary>
    Fixed,

    /// <summary>
    /// Extract the barcode from scanned page barcode metadata.
    /// </summary>
    FromScannedBarcode,

    /// <summary>
    /// Extract the barcode from the saved file name.
    /// </summary>
    FromFilename,

    /// <summary>
    /// Prompt the user for the barcode.
    /// </summary>
    PromptUser
}

/// <summary>
/// Specifies where the SAP barcode value should be obtained from for upload.
/// </summary>
public enum BarcodeSourceForSap
{
    /// <summary>
    /// Use a fixed configured barcode value.
    /// </summary>
    Fixed,

    /// <summary>
    /// Use the separator barcode or first detected barcode from ScanContext.
    /// </summary>
    FromContextBarcode,

    /// <summary>
    /// Resolve the configured barcode template using ScanContext placeholders.
    /// </summary>
    Template,

    /// <summary>
    /// Extract the barcode from scanned page barcode metadata.
    /// </summary>
    FromScannedBarcode,

    /// <summary>
    /// Extract the barcode from the saved file name.
    /// </summary>
    FromFilename,

    /// <summary>
    /// Prompt the user for the barcode.
    /// </summary>
    PromptUser
}

/// <summary>
/// Legacy object key source retained for compatibility with older ScanMe integration code.
/// </summary>
public enum ObjectKeySource
{
    /// <summary>
    /// Prompt the user for the object key.
    /// </summary>
    PromptUser,

    /// <summary>
    /// Extract the object key from scanned page barcode metadata.
    /// </summary>
    FromBarcode,

    /// <summary>
    /// Extract the object key from the saved file name.
    /// </summary>
    FromFilename,

    /// <summary>
    /// Use a fixed configured object key value.
    /// </summary>
    Fixed
}

/// <summary>
/// Contains per-profile SAP ArchiveLink OData upload settings embedded in a ScanMe scan profile.
/// </summary>
public class SapArchiveProfileSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether SAP ArchiveLink upload is enabled for this profile.
    /// </summary>
    public bool EnableUpload { get; set; }

    /// <summary>
    /// Gets or sets the SAP archive/content repository ID, for example <c>PS</c>.
    /// </summary>
    public string? ArchiveId { get; set; }

    /// <summary>
    /// Gets or sets the source used to determine the barcode header value.
    /// </summary>
    public BarcodeSource BarcodeSource { get; set; } = BarcodeSource.PromptUser;

    /// <summary>
    /// Gets or sets the fixed barcode used when <see cref="BarcodeSource" /> is <see cref="BarcodeSource.Fixed" />.
    /// </summary>
    public string? FixedBarcode { get; set; }

    /// <summary>
    /// Gets or sets the optional regular expression used for <see cref="BarcodeSource.FromScannedBarcode" /> or <see cref="BarcodeSource.FromFilename" />.
    /// </summary>
    public string? BarcodeRegex { get; set; }

    /// <summary>
    /// Gets or sets the optional ArchiveLink/BOR object type header value, for example <c>BUS2012</c>.
    /// </summary>
    public string? ArObject { get; set; }

    /// <summary>
    /// Gets or sets the optional SAP business object header value.
    /// </summary>
    public string? SapObject { get; set; }

    /// <summary>
    /// Gets or sets the profile-specific SAP OData connection. Passwords are stored only in encrypted form.
    /// </summary>
    public SapConnectionConfig? Connection { get; set; }

    /// <summary>
    /// Gets or sets the name of the global SAP connection used by this profile.
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Gets or sets the barcode template used when <see cref="BarcodeSourceForSap" /> is <see cref="BarcodeSourceForSap.Template" />.
    /// </summary>
    public string? BarcodeTemplate { get; set; }

    /// <summary>
    /// Gets or sets an optional slug/file-name override template.
    /// </summary>
    public string? SlugTemplate { get; set; }

    /// <summary>
    /// Gets or sets the optional SAP object ID header value. May contain placeholders such as <c>{barcode}</c>.
    /// </summary>
    public string? ObjectId { get; set; }

    /// <summary>
    /// Gets or sets the optional SAP object ID template. May contain placeholders such as <c>$(barcode)</c>.
    /// </summary>
    public string? ObjectIdTemplate
    {
        get => ObjectId;
        set => ObjectId = value;
    }

    /// <summary>
    /// Gets or sets the barcode source used by SAP upload. Alias for <see cref="BarcodeSource" />.
    /// </summary>
    public BarcodeSourceForSap BarcodeSourceForSap
    {
        get => BarcodeSource switch
        {
            BarcodeSource.Fixed => BarcodeSourceForSap.Fixed,
            BarcodeSource.FromScannedBarcode => BarcodeSourceForSap.FromContextBarcode,
            BarcodeSource.FromFilename => BarcodeSourceForSap.FromFilename,
            _ => BarcodeSourceForSap.FromContextBarcode
        };
        set => BarcodeSource = value switch
        {
            BarcodeSourceForSap.Fixed => BarcodeSource.Fixed,
            BarcodeSourceForSap.Template => BarcodeSource.PromptUser,
            BarcodeSourceForSap.FromScannedBarcode => BarcodeSource.FromScannedBarcode,
            BarcodeSourceForSap.FromFilename => BarcodeSource.FromFilename,
            BarcodeSourceForSap.PromptUser => BarcodeSource.PromptUser,
            _ => BarcodeSource.FromScannedBarcode
        };
    }

    /// <summary>
    /// Legacy property retained for compatibility. Maps to <see cref="ArObject" />.
    /// </summary>
    public string? SapObjectType
    {
        get => ArObject;
        set => ArObject = value;
    }

    /// <summary>
    /// Legacy property retained for compatibility. OData upload does not send this value directly.
    /// </summary>
    public string? ArDocType { get; set; }

    /// <summary>
    /// Legacy property retained for compatibility. Maps to <see cref="BarcodeSource" />.
    /// </summary>
    public ObjectKeySource ObjectKeySource
    {
        get => BarcodeSource switch
        {
            BarcodeSource.Fixed => ObjectKeySource.Fixed,
            BarcodeSource.FromScannedBarcode => ObjectKeySource.FromBarcode,
            BarcodeSource.FromFilename => ObjectKeySource.FromFilename,
            _ => ObjectKeySource.PromptUser
        };
        set => BarcodeSource = value switch
        {
            ObjectKeySource.Fixed => BarcodeSource.Fixed,
            ObjectKeySource.FromBarcode => BarcodeSource.FromScannedBarcode,
            ObjectKeySource.FromFilename => BarcodeSource.FromFilename,
            _ => BarcodeSource.PromptUser
        };
    }

    /// <summary>
    /// Legacy property retained for compatibility. Maps to <see cref="FixedBarcode" />.
    /// </summary>
    public string? FixedObjectKey
    {
        get => FixedBarcode;
        set => FixedBarcode = value;
    }

    /// <summary>
    /// Legacy property retained for compatibility. Maps to <see cref="BarcodeRegex" />.
    /// </summary>
    public string? FilenameRegex
    {
        get => BarcodeRegex;
        set => BarcodeRegex = value;
    }

    /// <summary>
    /// Legacy property retained for compatibility. OData upload does not send descriptions in part 2.
    /// </summary>
    public string? DescriptionTemplate { get; set; }

    /// <summary>
    /// Validates the profile settings for configuration completeness and consistency. Problems are
    /// returned as codes rather than text, because this assembly has no localized resources -- the UI
    /// layer turns them into messages in the operator's language.
    /// </summary>
    /// <returns>A list of validation problems. An empty list indicates success.</returns>
    public IReadOnlyList<SapSettingsIssue> Validate()
    {
        var problems = new List<SapSettingsIssue>();
        if (!EnableUpload)
        {
            return problems;
        }

        if (string.IsNullOrWhiteSpace(ArchiveId))
        {
            problems.Add(new SapSettingsIssue(SapSettingsProblem.ArchiveIdMissing));
        }
        var connection = Connection;
        if (connection != null)
        {
            if (string.IsNullOrWhiteSpace(connection.Host) || !connection.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new SapSettingsIssue(SapSettingsProblem.HostMissingOrNotHttps));
            }
            if (string.IsNullOrWhiteSpace(connection.ServiceName))
            {
                problems.Add(new SapSettingsIssue(SapSettingsProblem.ServiceNameMissing));
            }
            if (string.IsNullOrWhiteSpace(connection.Client) || connection.Client.Length != 3)
            {
                problems.Add(new SapSettingsIssue(SapSettingsProblem.ClientNotThreeDigits));
            }
            if (string.IsNullOrWhiteSpace(connection.User))
            {
                problems.Add(new SapSettingsIssue(SapSettingsProblem.UserMissing));
            }
        }
        if (BarcodeSource == BarcodeSource.Fixed && string.IsNullOrWhiteSpace(FixedBarcode))
        {
            problems.Add(new SapSettingsIssue(SapSettingsProblem.FixedBarcodeMissing));
        }
        if (!string.IsNullOrWhiteSpace(BarcodeRegex))
        {
            try
            {
                _ = new Regex(BarcodeRegex);
            }
            catch (ArgumentException ex)
            {
                problems.Add(new SapSettingsIssue(SapSettingsProblem.BarcodeRegexInvalid, ex.Message));
            }
        }
        return problems;
    }
}

/// <summary>
/// A configuration problem found by <see cref="SapArchiveProfileSettings.Validate"/>.
/// </summary>
public enum SapSettingsProblem
{
    ArchiveIdMissing,
    HostMissingOrNotHttps,
    ServiceNameMissing,
    ClientNotThreeDigits,
    UserMissing,
    FixedBarcodeMissing,
    BarcodeRegexInvalid
}

/// <summary>
/// One validation problem, plus any detail that only the check itself knows (such as the regex parser's
/// complaint). The UI turns this into a localized message.
/// </summary>
public class SapSettingsIssue
{
    public SapSettingsIssue(SapSettingsProblem problem, string? detail = null)
    {
        Problem = problem;
        Detail = detail;
    }

    public SapSettingsProblem Problem { get; }

    public string? Detail { get; }
}
