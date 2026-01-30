using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

public interface IDesktopScanController
{
    Task ScanWithDevice(string deviceID);
    Task ScanDefault();
    Task ScanWithNewProfile();
    Task ScanWithProfile(ScanProfile profile);
    // Expose the current default profile so other components (e.g., SharePoint upload) can access profile settings
    ScanProfile? DefaultProfile { get; }
}