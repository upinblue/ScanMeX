using NAPS2.ImportExport;
using NAPS2.Scan;

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

    /// <summary>
    /// Why the upload failed, for the caller to report. Null while the operation is running or if it succeeded.
    /// </summary>
    public string? FailureMessage { get; private set; }

    public bool Start(SharePointUploadSettings settings, string localFilePath, string fileName)
    {
        _settings = settings;
        _localFilePath = localFilePath;
        _fileName = fileName;

        ProgressTitle = UiStrings.SharePointUploadTitle;
        Status = new OperationStatus
        {
            StatusText = string.Format(UiStrings.SharePointUploadPreparing, _fileName),
            MaxProgress = 100
        };

        RunAsync(async () =>
        {
            try
            {
                // Inline rather than Progress<int>: the upload runs without a synchronization context, so
                // Progress<T> would post these to the thread pool and they could arrive out of order or
                // after the upload has finished. See InlineProgress.
                var progress = new InlineProgress<int>(percent =>
                {
                    // Map progress into human-readable stages and log debug info
                    Status.CurrentProgress = percent;
                    if (percent < 10)
                    {
                        Status.StatusText = UiStrings.SharePointAuthenticating;
                    }
                    else if (percent < 30)
                    {
                        Status.StatusText = UiStrings.SharePointResolvingSite;
                    }
                    else if (percent < 40)
                    {
                        Status.StatusText = UiStrings.SharePointResolvingLibrary;
                    }
                    else if (percent < 100)
                    {
                        Status.StatusText = string.Format(UiStrings.SharePointUploadingFile, _fileName, percent);
                    }
                    // Deliberately not logged per percent -- the stage changes below are what matters,
                    // and a line per percent would bury everything else in the console.
                    InvokeStatusChanged();
                });

                await _uploadService.UploadFileAsync(_settings, _localFilePath, _fileName, progress, CancelToken);
                ScanConsole.Upload("[SP] Upload succeeded");
                return true;
            }
            catch (OperationCanceledException)
            {
                ScanConsole.Upload("[SP] Upload canceled");
                FailureMessage = UiStrings.UploadCancelled;
                return false;
            }
            catch (Exception ex)
            {
                ScanConsole.Upload($"[SP] Upload failed: {ex.Message}");
                // The caller reports this to the operator; raising Error too would show a second message
                // for the same failure whenever the operation is created through the operation factory.
                FailureMessage = ex.Message;
                Log.ErrorException("SharePoint upload failed", ex);
                return false;
            }
        });

        return true;
    }
}
