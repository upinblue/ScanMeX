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
                    ? $"SAP-Upload OK – DocId: {Result.ArchivDocId}, Barcode: {_request.Barcode}"
                    : $"SAP-Upload fehlgeschlagen – HTTP: {Result.HttpStatusCode}, Code: {Result.ErrorCode}, Message: {Result.ErrorMessage}, TransactionId: {Result.TransactionId}";
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
                InvokeError(SapUi.UploadTitle, new InvalidOperationException(Status.StatusText));
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

    private static string? Truncate(string? value)
    {
        return value == null || value.Length <= 4096 ? value : value.Substring(0, 4096) + "...";
    }
}
