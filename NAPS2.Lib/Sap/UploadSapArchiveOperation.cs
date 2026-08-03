using Microsoft.Extensions.Logging;
using NAPS2.Operation;

namespace NAPS2.Sap;

internal class UploadSapArchiveOperation : OperationBase
{
    private SapUploadRequest _request = null!;
    private ISapArchiveUploader _uploader = null!;

    public SapUploadResult? Result { get; private set; }

    /// <summary>
    /// Why the upload failed, for the caller to report. Null while the operation is running or if it succeeded.
    /// </summary>
    public string? FailureMessage { get; private set; }

    public UploadSapArchiveOperation()
    {
        AllowCancel = true;
        AllowBackground = true;
    }

    public bool Start(ISapArchiveUploader uploader, SapUploadRequest request)
    {
        _uploader = uploader;
        _request = request;
        ProgressTitle = UiStrings.SapUploadTitle;
        Status = new OperationStatus
        {
            StatusText = string.Format(UiStrings.SapUploadPreparing, request.FileName),
            MaxProgress = 100
        };

        RunAsync(async () =>
        {
            try
            {
                Status.CurrentProgress = 20;
                Status.StatusText = UiStrings.SapUploading;
                InvokeStatusChanged();

                Result = await _uploader.UploadAsync(_request, CancelToken);
                Status.CurrentProgress = 100;
                Status.StatusText = Result.Success
                    ? string.Format(UiStrings.SapTestUploadSucceeded, Result.ArchivDocId, _request.Barcode)
                    : string.Format(UiStrings.SapTestUploadFailed, Result.HttpStatusCode, Result.ErrorCode,
                        Result.ErrorMessage, Result.TransactionId);
                InvokeStatusChanged();

                if (Result.Success)
                {
                    Log.Logger.LogInformation("SAP ArchiveLink upload succeeded for {FileName}. DocId={DocId}, Barcode={Barcode}",
                        _request.FileName, Result.ArchivDocId, _request.Barcode);
                    return true;
                }

                Log.Logger.LogError("SAP ArchiveLink upload failed for {FileName}. HTTP={HttpStatusCode}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}, TransactionId={TransactionId}, Body={Body}",
                    _request.FileName, Result.HttpStatusCode, Result.ErrorCode, Result.ErrorMessage, Result.TransactionId,
                    Truncate(Result.RawResponseBody));
                // The caller reports this to the operator. Error is only wired up for operations created
                // through the operation factory, which this one isn't, so it can't be the only channel.
                FailureMessage = string.Format(UiStrings.SapUploadErrorDetail, Result.HttpStatusCode,
                    Result.ErrorCode, Result.ErrorMessage, Result.TransactionId);
                return false;
            }
            catch (OperationCanceledException)
            {
                FailureMessage = UiStrings.UploadCancelled;
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorException("SAP ArchiveLink upload failed", ex);
                FailureMessage = ex.Message;
                return false;
            }
        });
        return true;
    }

    private static string? Truncate(string? value)
    {
        return value == null || value.Length <= 4096 ? value : value.Substring(0, 4096) + "...";
    }
}
