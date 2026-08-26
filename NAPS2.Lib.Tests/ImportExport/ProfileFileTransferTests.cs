using NAPS2.ImportExport.Profiles;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.ImportExport;

/// <summary>
/// What a profile carried to another machine takes with it, and what it must not.
/// </summary>
public class ProfileFileTransferTests : ContextualTests
{
    private readonly ProfileFileTransfer _transfer = new();

    [Fact]
    public void TheSapPasswordAndTheSharePointSecretAreNotInTheFile()
    {
        var path = ExportPath();

        _transfer.Export([ProfileWithSecrets()], path);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("dpapi-payload-from-this-machine", text);
        Assert.DoesNotContain("the-client-secret", text);
        // The elements are still written, as nil -- what matters is that neither carries a value.
        var doc = XDocument.Load(path);
        Assert.All(doc.Descendants("EncryptedPassword"), x => Assert.Empty(x.Value));
        Assert.All(doc.Descendants("ClientSecret"), x => Assert.Empty(x.Value));
    }

    [Fact]
    public void AnImportedProfileArrivesWithoutEitherSecret()
    {
        var path = ExportPath();
        _transfer.Export([ProfileWithSecrets()], path);

        var imported = _transfer.Import(path).Single();

        Assert.True(string.IsNullOrEmpty(imported.SapArchiveSettings!.Connection!.EncryptedPassword));
        Assert.True(string.IsNullOrEmpty(imported.SharePointUploadSettings.ClientSecret));
    }

    [Fact]
    public void ASecretInTheFileIsStillNotInstalled()
    {
        // A profiles.xml lifted straight out of AppData, rather than one this class wrote. The SAP
        // password in it can't be decrypted here, and the SharePoint secret was never meant to travel.
        var path = ExportPath();
        var withSecrets = ProfileWithSecrets();
        using (var stream = new FileStream(path, FileMode.Create))
        {
            new NAPS2.Config.ProfileSerializer().Serialize(stream,
                new NAPS2.Config.Model.ConfigStorage<System.Collections.Immutable.ImmutableList<ScanProfile>>(
                    [withSecrets]));
        }
        Assert.Contains("the-client-secret", File.ReadAllText(path));

        var imported = _transfer.Import(path).Single();

        Assert.True(string.IsNullOrEmpty(imported.SapArchiveSettings!.Connection!.EncryptedPassword));
        Assert.True(string.IsNullOrEmpty(imported.SharePointUploadSettings.ClientSecret));
    }

    [Fact]
    public void EverythingElseSurvivesTheRoundTrip()
    {
        var path = ExportPath();
        _transfer.Export([ProfileWithSecrets()], path);

        var imported = _transfer.Import(path).Single();

        Assert.Equal("Process orders", imported.DisplayName);
        Assert.Equal("device_id", imported.Device!.ID);
        Assert.Equal("escl", imported.DriverName);
        Assert.Equal(400, imported.Resolution.Dpi);

        var workflow = imported.DocumentWorkflow!;
        Assert.Equal(DocumentSeparationMode.Barcode, workflow.SeparationMode);
        Assert.Equal("^4[0-9]{6}$", workflow.SeparationPattern);
        Assert.Equal([BarcodeSymbology.Code39], workflow.BarcodeSymbologies);
        Assert.True(workflow.SaveLocally);
        Assert.Equal(@"C:\Scans", workflow.LocalFolder);
        Assert.Equal(UploadTrigger.Manual, workflow.UploadTrigger);

        var sharePoint = imported.SharePointUploadSettings;
        Assert.Equal("https://contoso.sharepoint.com/sites/Scans", sharePoint.SiteUrl);
        Assert.Equal("Documents", sharePoint.LibraryNameOrPath);
        Assert.Equal("$(barcode)", sharePoint.FolderPath);
        Assert.Equal("tenant-id", sharePoint.TenantId);
        Assert.Equal("client-id", sharePoint.ClientId);

        var sap = imported.SapArchiveSettings!;
        Assert.True(sap.EnableUpload);
        Assert.Equal("PS", sap.ArchiveId);
        Assert.Equal("https://sap.example.com", sap.Connection!.Host);
        Assert.Equal("ZARCHIVE_UPLOAD_SRV", sap.Connection.ServiceName);
        Assert.Equal("100", sap.Connection.Client);
        Assert.Equal("DE", sap.Connection.Language);
        Assert.Equal("SCANUSER", sap.Connection.User);
        Assert.Equal(45, sap.Connection.ConnectTimeoutSeconds);
        Assert.Equal(600, sap.Connection.UploadTimeoutSeconds);
        Assert.True(sap.Connection.IgnoreCertificateErrors);
    }

    [Fact]
    public void ALockedProfileArrivesUnlocked()
    {
        // Locked says something about the administrator's profiles file on the machine it came from.
        var profile = ProfileWithSecrets();
        profile.IsLocked = true;
        profile.IsDeviceLocked = true;
        var path = ExportPath();
        _transfer.Export([profile], path);

        var imported = _transfer.Import(path).Single();

        Assert.False(imported.IsLocked);
        Assert.False(imported.IsDeviceLocked);
    }

    [Fact]
    public void ProfilesComeBackInTheOrderTheyWereWritten()
    {
        var path = ExportPath();
        _transfer.Export([Named("One"), Named("Two"), Named("Three")], path);

        Assert.Equal(["One", "Two", "Three"], _transfer.Import(path).Select(x => x.DisplayName));
    }

    [Fact]
    public void AFreeNameIsLeftAlone()
    {
        Assert.Null(ProfileFileTransfer.MakeNameUnique("Invoices", new HashSet<string> { "Orders" }));
    }

    [Fact]
    public void ATakenNameIsNumberedUntilItIsFree()
    {
        var taken = new HashSet<string> { "Invoices", "Invoices (2)" };

        Assert.Equal("Invoices (3)", ProfileFileTransfer.MakeNameUnique("Invoices", taken));
    }

    private string ExportPath() => Path.Combine(FolderPath, Path.GetRandomFileName());

    private static ScanProfile Named(string name) => new() { Version = ScanProfile.CURRENT_VERSION, DisplayName = name };

    private static ScanProfile ProfileWithSecrets() => new()
    {
        Version = ScanProfile.CURRENT_VERSION,
        DisplayName = "Process orders",
        Device = new ScanProfileDevice("device_id", "device_name"),
        DriverName = "escl",
        Resolution = new ScanResolution { Dpi = 400 },
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = DocumentSeparationMode.Barcode,
            SeparationPattern = "^4[0-9]{6}$",
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SaveLocally = true,
            LocalFolder = @"C:\Scans",
            UploadTrigger = UploadTrigger.Manual
        },
        EnableSharePointUpload = true,
        SharePointUploadSettings = new SharePointUploadSettings
        {
            SiteUrl = "https://contoso.sharepoint.com/sites/Scans",
            LibraryNameOrPath = "Documents",
            FolderPath = "$(barcode)",
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "the-client-secret"
        },
        SapArchiveSettings = new SapArchiveProfileSettings
        {
            EnableUpload = true,
            ArchiveId = "PS",
            BarcodeSource = BarcodeSource.FromScannedBarcode,
            Connection = new SapConnectionConfig
            {
                Host = "https://sap.example.com",
                ServiceName = "ZARCHIVE_UPLOAD_SRV",
                Client = "100",
                Language = "DE",
                User = "SCANUSER",
                EncryptedPassword = "dpapi-payload-from-this-machine",
                IgnoreCertificateErrors = true,
                ConnectTimeoutSeconds = 45,
                UploadTimeoutSeconds = 600
            }
        }
    };
}
