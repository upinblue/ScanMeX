using NAPS2.ImportExport;
using NAPS2.Scan;
using Eto.Forms;

namespace NAPS2.SharePoint;

internal class UploadSharePointOperation : OperationBase
{
    private readonly SharePointUploadService _uploadService;
    private string _localFilePath = null!;
    private string _fileName = null!;
    private SharePointUploadSettings _settings = null!;

    public UploadSharePointOperation(SharePointUploadService uploadService)
    {
        _uploadService = uploadService;
        AllowCancel = true;
        AllowBackground = true;
    }

    public bool Start(SharePointUploadSettings settings, string localFilePath, string fileName)
    {
        _settings = settings;
        _localFilePath = localFilePath;
        _fileName = fileName;

        ProgressTitle = "Upload to SharePoint";
        Status = new OperationStatus
        {
            StatusText = $"Preparing upload for {_fileName}",
            MaxProgress = 100
        };

        RunAsync(async () =>
        {
            try
            {
                var progress = new Progress<int>(percent =>
                {
                    // Map progress into human-readable stages and log debug info
                    Status.CurrentProgress = percent;
                    if (percent < 10)
                    {
                        Status.StatusText = "Authenticating with Microsoft Graph";
                    }
                    else if (percent < 30)
                    {
                        Status.StatusText = "Resolving SharePoint site";
                    }
                    else if (percent < 40)
                    {
                        Status.StatusText = "Resolving document library";
                    }
                    else if (percent < 100)
                    {
                        Status.StatusText = $"Uploading {_fileName} ({percent}%)";
                    }
                    Debug.WriteLine($"[SP][OP] progress={percent}, status='{Status.StatusText}'");
                    InvokeStatusChanged();
                });

                await _uploadService.UploadFileAsync(_settings, _localFilePath, _fileName, progress, CancelToken);
                Debug.WriteLine("[SP][OP] Upload succeeded");
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[SP][OP] Upload canceled");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SP][OP] Upload failed: {ex.Message}\n{ex}");
                InvokeError("SharePoint upload failed", ex);
                // Also show an immediate UI error prompt for clarity on the UI thread
                try
                {
                    Invoker.Current.Invoke(() =>
                    {
                        if (Application.Instance?.MainForm != null)
                        {
                            MessageBox.Show(Application.Instance.MainForm,
                                $"SharePoint upload failed: {ex.Message}",
                                "Upload to SharePoint", MessageBoxButtons.OK, MessageBoxType.Error);
                        }
                    });
                }
                catch
                {
                    // Ignore UI errors
                }
                return false;
            }
        });

        return true;
    }
}
