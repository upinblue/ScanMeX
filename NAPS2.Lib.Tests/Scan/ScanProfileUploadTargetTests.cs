#nullable enable
using NAPS2.Sap;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Enabling an upload target used to be possible in two dialogs writing two different flags, and the
/// runtime only honoured one of them. These pin down that either flag counts.
/// </summary>
public class ScanProfileUploadTargetTests
{
    [Fact]
    public void SharePoint_ProfileFlagAlone_CountsAsEnabled()
    {
        var profile = new ScanProfile
        {
            EnableSharePointUpload = true,
            AutoSaveSettings = new AutoSaveSettings { UploadToSharePoint = false }
        };

        Assert.True(profile.UploadsToSharePoint());
    }

    [Fact]
    public void SharePoint_AutoSaveFlagAlone_CountsAsEnabled()
    {
        var profile = new ScanProfile
        {
            EnableSharePointUpload = false,
            AutoSaveSettings = new AutoSaveSettings { UploadToSharePoint = true }
        };

        Assert.True(profile.UploadsToSharePoint());
    }

    [Fact]
    public void SharePoint_NeitherFlag_IsDisabled()
    {
        var profile = new ScanProfile
        {
            EnableSharePointUpload = false,
            AutoSaveSettings = new AutoSaveSettings()
        };

        Assert.False(profile.UploadsToSharePoint());
    }

    [Fact]
    public void Sap_EitherFlag_CountsAsEnabled()
    {
        var viaSettings = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings { EnableUpload = true }
        };
        var viaAutoSave = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings { EnableUpload = false },
            AutoSaveSettings = new AutoSaveSettings { UploadToSap = true }
        };

        Assert.True(viaSettings.UploadsToSap());
        Assert.True(viaAutoSave.UploadsToSap());
    }

    [Fact]
    public void Sap_WithoutSettings_IsDisabled()
    {
        // No SAP configuration at all, so there is nowhere to upload to even if the flag says otherwise.
        var profile = new ScanProfile
        {
            SapArchiveSettings = null,
            AutoSaveSettings = new AutoSaveSettings { UploadToSap = true }
        };

        Assert.False(profile.UploadsToSap());
    }
}
