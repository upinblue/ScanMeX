#nullable enable
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.SharePoint;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Whether the scan decodes barcodes at all. The obvious triggers are separation and the SAP object key;
/// the one that used to be missed is a template. A profile whose auto save path is <c>$(barcode).pdf</c>
/// but that doesn't separate got no detection, so the placeholder expanded to nothing and the document
/// was written under a name with a hole in it -- a silent wrong result rather than a visible failure.
/// </summary>
public class BarcodeDetectionGateTests
{
    [Fact]
    public void PlainProfile_NeedsNoBarcodes()
    {
        Assert.False(new ScanProfile().NeedsBarcodeValues());
    }

    [Fact]
    public void BarcodeSeparation_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39]
            }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void PatchTSeparation_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings { SeparationMode = DocumentSeparationMode.PatchT }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void SapObjectKeyFromTheScannedBarcode_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings
            {
                EnableUpload = true,
                BarcodeSource = BarcodeSource.FromScannedBarcode
            }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void AutoSavePathWithABarcodePlaceholder_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(barcode).pdf" }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void NumberedBarcodePlaceholder_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(barcode:2).pdf" }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void SharePointFolderWithABarcodePlaceholder_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            EnableSharePointUpload = true,
            SharePointUploadSettings = new SharePointUploadSettings { FolderPath = "$(barcode)" }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void SapObjectIdTemplateWithABarcodePlaceholder_NeedsBarcodes()
    {
        var profile = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings
            {
                EnableUpload = true,
                BarcodeSource = BarcodeSource.PromptUser,
                ObjectId = "$(barcode)"
            }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    /// <summary>
    /// $(id) falls back to the barcode unless the operator is asked to type the number, so it needs
    /// detection in exactly the cases where it isn't going to be typed.
    /// </summary>
    [Fact]
    public void IdPlaceholder_NeedsBarcodesUnlessTheOperatorTypesIt()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(id).pdf" }
        };
        Assert.True(profile.NeedsBarcodeValues());

        profile.DocumentWorkflow = new DocumentWorkflowSettings { IdMode = DocumentIdMode.ManualInput };
        Assert.False(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void PlaceholderMatchingIsCaseInsensitive()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(BARCODE).pdf" }
        };

        Assert.True(profile.NeedsBarcodeValues());
    }

    [Fact]
    public void ADatePlaceholderAloneDoesNotTurnDetectionOn()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(yyyy-MM-dd).pdf" }
        };

        Assert.False(profile.NeedsBarcodeValues());
    }
}
