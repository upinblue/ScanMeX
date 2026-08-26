using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAPS2.Operation;

namespace NAPS2.Sap;

internal class UploadSapArchiveOperation : OperationBase
{
    /// <summary>
    /// Where the bar creeps to while SAP archives the document. Never reached, and never the end: only a
    /// finished upload shows a full bar.
    /// </summary>
    private const int CrawlTargetPercent = 95;

    /// <summary>
    /// How quickly the crawl approaches its target. At 25 seconds it has covered about two thirds of the
    /// way, at a minute about nine tenths -- fast enough to look alive on a quick archive, slow enough
    /// that a long one still has somewhere left to go.
    /// </summary>
    private const double CrawlTimeConstantSeconds = 25;

    private static readonly TimeSpan CrawlTick = TimeSpan.FromMilliseconds(500);

    private readonly object _statusLock = new();
    private SapUploadRequest _request = null!;
    private ISapArchiveUploader _uploader = null!;
    private System.Threading.Timer? _crawlTimer;
    private Stopwatch? _waitWatch;
    private int _crawlStartPercent;

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
                // Reports the same way the SharePoint upload does -- a share of the bar for signing in,
                // then the document's bytes, then the wait while the target system processes it. Before
                // this the bar jumped to 20% and stopped there for the whole upload, which is
                // indistinguishable from an upload that has hung.
                Result = await _uploader.UploadAsync(_request,
                    new InlineProgress<SapUploadProgress>(ReportStage), CancelToken);
                StopCrawl();
                lock (_statusLock)
                {
                    Status.CurrentProgress = 100;
                    Status.StatusText = Result.Success
                        ? string.Format(UiStrings.SapTestUploadSucceeded, Result.ArchivDocId, _request.Barcode)
                        : DescribeFailure(Result);
                }
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
                FailureMessage = DescribeFailure(Result);
                return false;
            }
            catch (OperationCanceledException)
            {
                // Only ever the operator now. The uploader used to let its own expired deadline out of
                // here as a TaskCanceledException, so a timeout was reported as a cancelled upload.
                FailureMessage = UiStrings.UploadCancelled;
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorException("SAP ArchiveLink upload failed", ex);
                FailureMessage = ex.Message;
                return false;
            }
            finally
            {
                StopCrawl();
            }
        });
        return true;
    }

    /// <summary>
    /// What the operator is told about a failure. A timeout gets its own sentence because the technical
    /// one -- an empty HTTP status and an exception name -- says neither what happened nor what to do
    /// about it, and because it is the one failure that leaves the document's fate genuinely open.
    /// </summary>
    private string DescribeFailure(SapUploadResult result) => result.ErrorCode switch
    {
        HttpSapArchiveUploader.TimeoutErrorCode => string.Format(UiStrings.SapUploadTimedOut,
            _request.FileName, (int) _request.Connection.GetUploadTimeout().TotalSeconds),
        HttpSapArchiveUploader.SignInTimeoutErrorCode => string.Format(UiStrings.SapSignInTimedOut,
            (int) _request.Connection.GetConnectTimeout().TotalSeconds),
        _ => string.Format(UiStrings.SapUploadErrorDetail, result.HttpStatusCode, result.ErrorCode,
            result.ErrorMessage, result.TransactionId)
    };

    /// <summary>
    /// Turns an uploader stage into what the operator sees. The percentage is clamped below 100 so only a
    /// finished upload ever shows a full bar.
    /// </summary>
    private void ReportStage(SapUploadProgress progress)
    {
        if (progress.Stage == SapUploadStage.WaitingForSap)
        {
            StartCrawl(progress.Percent);
            return;
        }
        StopCrawl();
        lock (_statusLock)
        {
            Status.CurrentProgress = Math.Clamp(progress.Percent, 0, 99);
            Status.StatusText = progress.Stage switch
            {
                SapUploadStage.Preparing => string.Format(UiStrings.SapUploadPreparing, _request.FileName),
                SapUploadStage.Authenticating => UiStrings.SapAuthenticating,
                SapUploadStage.Uploading => string.Format(UiStrings.SapUploadingFile, _request.FileName,
                    Status.CurrentProgress),
                SapUploadStage.Retrying => string.Format(UiStrings.SapUploadRetrying, _request.FileName),
                _ => UiStrings.SapUploading
            };
        }
        InvokeStatusChanged();
    }

    /// <summary>
    /// Carries the bar through the wait that nobody reports on.
    /// </summary>
    /// <remarks>
    /// Once the document has been handed to the socket, SAP archives it and says nothing until it is
    /// done -- which is most of the wall-clock time of an upload and used to be all of it spent at one
    /// unchanging number. There is no progress to report, so none is invented: the bar eases towards
    /// <see cref="CrawlTargetPercent" /> without ever arriving, and the status line counts the seconds
    /// out loud, which is the honest answer to "is this still going?".
    /// </remarks>
    private void StartCrawl(int fromPercent)
    {
        lock (_statusLock)
        {
            if (_waitWatch != null)
            {
                return;
            }
            _crawlStartPercent = Math.Clamp(fromPercent, 0, CrawlTargetPercent);
            _waitWatch = Stopwatch.StartNew();
            Status.CurrentProgress = _crawlStartPercent;
            Status.StatusText = string.Format(UiStrings.SapWaitingForArchive, _request.FileName, 0);
            _crawlTimer = new System.Threading.Timer(_ => Crawl(), null, CrawlTick, CrawlTick);
        }
        InvokeStatusChanged();
    }

    private void Crawl()
    {
        lock (_statusLock)
        {
            if (_waitWatch == null)
            {
                return;
            }
            var seconds = _waitWatch.Elapsed.TotalSeconds;
            var eased = _crawlStartPercent +
                        (CrawlTargetPercent - _crawlStartPercent) * (1 - Math.Exp(-seconds / CrawlTimeConstantSeconds));
            // Never backwards, whatever else has moved the bar in the meantime.
            Status.CurrentProgress = Math.Max(Status.CurrentProgress, (int) eased);
            Status.StatusText = string.Format(UiStrings.SapWaitingForArchive, _request.FileName, (int) seconds);
        }
        InvokeStatusChanged();
    }

    private void StopCrawl()
    {
        System.Threading.Timer? timer;
        lock (_statusLock)
        {
            timer = _crawlTimer;
            _crawlTimer = null;
            _waitWatch = null;
        }
        timer?.Dispose();
    }

    private static string? Truncate(string? value)
    {
        return value == null || value.Length <= 4096 ? value : value.Substring(0, 4096) + "...";
    }
}
