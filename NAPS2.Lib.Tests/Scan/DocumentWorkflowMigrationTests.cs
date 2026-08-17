#nullable enable
using NAPS2.ImportExport;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Reading a profile that was saved before saving, uploading and the upload trigger became three
/// separate settings. Every one of these is a silent failure if it goes wrong: the operator opens the
/// app after an update, scans as usual, and the documents go somewhere else or nowhere at all.
/// </summary>
public class DocumentWorkflowMigrationTests
{
    /// <summary>
    /// The one that matters most. SaveLocally is not in an old profile's file, so taking the deserialized
    /// value at face value would read as "don't save" for a profile that had been saving all along.
    /// </summary>
    [Fact]
    public void AnOldProfileThatSavedKeepsSavingToTheSamePlace()
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(barcode).pdf" },
            // Written by a version that knew about the workflow but not about SaveLocally.
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39]
            }
        };

        var workflow = DocumentWorkflowSettings.ForProfile(profile);

        Assert.True(workflow.SaveLocally);
        Assert.Equal(@"C:\Scans", workflow.LocalFolder);
        Assert.Equal("$(barcode).pdf", workflow.DocumentNameTemplate);
        // The settings it did know about are left alone.
        Assert.Equal(DocumentSeparationMode.Barcode, workflow.SeparationMode);
        Assert.Equal([BarcodeSymbology.Code39], workflow.BarcodeSymbologies);
    }

    [Fact]
    public void AnOldProfileWithAutoSaveOffDoesNotStartSaving()
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = false,
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\out.pdf" }
        };

        Assert.False(DocumentWorkflowSettings.ForProfile(profile).SaveLocally);
    }

    /// <summary>
    /// "None" used to mean "whatever the auto save separator says", so a stored None on a profile whose
    /// separator was one-file-per-page meant per-page, not one document per scan. Reading it as the
    /// latter would silently merge a day's single-page documents into one file.
    /// </summary>
    [Theory]
    [InlineData(SaveSeparator.FilePerPage, DocumentSeparationMode.OnePerPage)]
    [InlineData(SaveSeparator.FilePerScan, DocumentSeparationMode.None)]
    [InlineData(SaveSeparator.PatchT, DocumentSeparationMode.PatchT)]
    [InlineData(SaveSeparator.Code39Barcode, DocumentSeparationMode.Barcode)]
    public void TheLegacySeparatorDecidesWhatNoneMeant(SaveSeparator separator, DocumentSeparationMode expected)
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\out.pdf", Separator = separator }
        };

        Assert.Equal(expected, DocumentWorkflowSettings.ForProfile(profile).SeparationMode);
    }

    /// <summary>
    /// A stored mode that isn't None was chosen deliberately and outranks the legacy separator.
    /// </summary>
    [Fact]
    public void AnExplicitlyStoredSeparationModeWins()
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings
            {
                FilePath = @"C:\Scans\out.pdf",
                Separator = SaveSeparator.FilePerPage
            },
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                SeparationMode = DocumentSeparationMode.Barcode
            }
        };

        Assert.Equal(DocumentSeparationMode.Barcode, DocumentWorkflowSettings.ForProfile(profile).SeparationMode);
    }

    /// <summary>
    /// The old manual-input mode aborted the save when the operator cancelled the prompt, so "no value
    /// entered" already meant "don't file this document". That has to survive as RequireIdentifier,
    /// otherwise the update would start filing documents under empty names.
    /// </summary>
    [Fact]
    public void ManualIdentificationStaysRequired()
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(id).pdf" },
            DocumentWorkflow = new DocumentWorkflowSettings { IdMode = DocumentIdMode.ManualInput }
        };

        Assert.True(DocumentWorkflowSettings.ForProfile(profile).RequireIdentifier);
    }

    /// <summary>
    /// Migration runs once. A profile written by the current version is taken as it stands, or an
    /// operator who deliberately turned local saving off would have it turned back on by the legacy
    /// EnableAutoSave flag on every load.
    /// </summary>
    [Fact]
    public void ACurrentProfileIsNotMigratedAgain()
    {
        var profile = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Old\out.pdf" },
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                SaveLocally = false,
                UploadTrigger = UploadTrigger.Manual
            }
        };

        var workflow = DocumentWorkflowSettings.ForProfile(profile);

        Assert.False(workflow.SaveLocally);
        Assert.Null(workflow.LocalFolder);
        Assert.Equal(UploadTrigger.Manual, workflow.UploadTrigger);
    }

    /// <summary>
    /// A profile that keeps nothing locally still has to name its documents, because that name is what
    /// SharePoint and the SAP archive store them under.
    /// </summary>
    [Fact]
    public void AProfileWithNoNameTemplateFallsBackToTheIdentifier()
    {
        var workflow = new DocumentWorkflowSettings();

        Assert.Equal("$(id).pdf", workflow.GetDocumentNameTemplate());
    }

    [Fact]
    public void APatchTProfileNeverKeepsTheSeparatorSheet()
    {
        var workflow = new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.PatchT,
            KeepSeparatorPage = true
        };

        Assert.False(workflow.KeepsSeparatorPage());
    }

    [Fact]
    public void BarcodeSeparationKeepsTheSheetThatCarriesTheOrderNumber()
    {
        var workflow = new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            KeepSeparatorPage = true
        };

        Assert.True(workflow.KeepsSeparatorPage());
    }

    /// <summary>
    /// Uploading no longer needs local saving. This combination -- keep nothing, archive on the button --
    /// is the one the whole split exists for.
    /// </summary>
    [Fact]
    public void UploadOnlyCountsAsPostScanWork()
    {
        var profile = new ScanProfile
        {
            EnableSharePointUpload = true,
            SharePointUploadSettings = new SharePointUploadSettings { SiteUrl = "https://x" },
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                SaveLocally = false,
                UploadTrigger = UploadTrigger.Manual
            }
        };

        Assert.True(DocumentWorkflowSettings.ForProfile(profile).HasPostScanWork(profile));
    }

    [Fact]
    public void AProfileWithNoDestinationHasNoPostScanWork()
    {
        var profile = new ScanProfile();

        Assert.False(DocumentWorkflowSettings.ForProfile(profile).HasPostScanWork(profile));
    }
}
