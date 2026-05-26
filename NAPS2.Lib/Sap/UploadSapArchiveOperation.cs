using Microsoft.Extensions.Logging;
using NAPS2.Operation;

namespace NAPS2.Sap;

internal class UploadSapArchiveOperation : OperationBase
{
    private SapUploadRequest _request = null!;
    private ISapArchiveUploader _uploader = null!;

    public SapUploadResult? Result { get; private set; }

    public UploadSapArchiveOperation()
    {
        AllowCancel = true;
        AllowBackground = true;
    }

    public bool Start(ISapArchiveUploader uploader, SapUploadRequest request)
    {
        _uploader = uploader;
        _request = request;
        ProgressTitle = SapUi.UploadTitle;
        Status = new OperationStatus
        {
            StatusText = SapUi.UploadPreparing(request.FileName),
            MaxProgress = 100
        };

        RunAsync(async () =>
        {
            try
            {
                Status.CurrentProgress = 20;
                Status.StatusText = SapUi.Uploading;
                InvokeStatusChanged();

                Result = await _uploader.UploadAsync(_request, CancelToken);
                Status.CurrentProgress = 100;
                Status.StatusText = Result.Success
                    ? SapUi.UploadSuccess(Result.ArchivDocId ?? "")
                    : SapUi.UploadFailed(Result.ErrorMessage ?? Result.ErrorCode ?? "Unknown error");
                InvokeStatusChanged();

                if (Result.Success)
                {
                    Log.Info(Status.StatusText);
                    return true;
                }

                Log.Logger.LogWarning("SAP ArchiveLink upload failed for {FileName}: {ErrorCode} {ErrorMessage}",
                    _request.FileName, Result.ErrorCode, Result.ErrorMessage);
                InvokeError(SapUi.UploadTitle, new InvalidOperationException(Result.ErrorMessage ?? Result.ErrorCode ?? "SAP ArchiveLink upload failed"));
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorException("SAP ArchiveLink upload failed", ex);
                InvokeError(SapUi.UploadTitle, ex);
                return false;
            }
        });
        return true;
    }
}
