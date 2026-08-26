#nullable enable
using System.Text;
using System.Threading;
using NAPS2.Sap;
using Xunit;

namespace NAPS2.Lib.Tests.Sap;

/// <summary>
/// What the progress window shows while a document goes to SAP.
/// </summary>
/// <remarks>
/// The wait for SAP to archive a document is most of the wall-clock time of an upload and the only part
/// nothing reports on: measured on a 3 MB document, the bar crossed from 20% to 90% in a single
/// millisecond and then stood at 90% for the remaining eight seconds -- 92% of the wait spent at one
/// unchanging number, which is exactly what an upload that has hung looks like.
/// </remarks>
public class SapUploadOperationProgressTests
{
    [Fact]
    public async Task TheBarKeepsMovingWhileSapArchivesTheDocument()
    {
        var gate = new TaskCompletionSource<bool>();
        var op = new UploadSapArchiveOperation();
        op.Start(new StubUploader(WaitForSapThen(gate, Success)), CreateRequest());

        var readings = await SampleWhileWaiting(op);
        gate.SetResult(true);
        await op.Success;

        Assert.True(readings.Distinct().Count() >= 3,
            $"The bar has to keep moving while SAP works; it read {string.Join(", ", readings)}.");
        Assert.Equal(readings.OrderBy(x => x), readings);
    }

    /// <summary>
    /// No progress is invented past the point of honesty: a full bar means the document is filed.
    /// </summary>
    [Fact]
    public async Task TheBarNeverFillsBeforeSapHasAnswered()
    {
        var gate = new TaskCompletionSource<bool>();
        var op = new UploadSapArchiveOperation();
        op.Start(new StubUploader(WaitForSapThen(gate, Success)), CreateRequest());

        var readings = await SampleWhileWaiting(op);
        gate.SetResult(true);
        await op.Success;

        Assert.All(readings, reading => Assert.InRange(reading, 45, 99));
        Assert.Equal(100, op.Status.CurrentProgress);
    }

    /// <summary>
    /// The seconds are the honest part. A bar that moves without anything happening is a guess; a count
    /// of how long SAP has been thinking is the actual answer to "is this still going?".
    /// </summary>
    [Fact]
    public async Task TheStatusLineCountsTheSecondsSapHasBeenWorking()
    {
        var gate = new TaskCompletionSource<bool>();
        var op = new UploadSapArchiveOperation();
        op.Start(new StubUploader(WaitForSapThen(gate, Success)), CreateRequest());

        await SampleWhileWaiting(op);
        var whileWaiting = op.Status.StatusText;
        gate.SetResult(true);
        await op.Success;

        Assert.Contains("scan.pdf", whileWaiting);
        Assert.Matches(@"\d+ s", whileWaiting);
    }

    /// <summary>
    /// A timeout used to reach the operator as "HTTP  TaskCanceledException: The request was canceled due
    /// to the configured HttpClient.Timeout of 60 seconds elapsing. (transaction )" -- an empty status
    /// code, an exception name, and no hint that the document's fate is genuinely open.
    /// </summary>
    [Fact]
    public async Task ATimeoutIsExplainedRatherThanQuotedAtTheOperator()
    {
        var op = new UploadSapArchiveOperation();
        op.Start(new StubUploader(_ => Task.FromResult(
            new SapUploadResult(false, null, null, null, HttpSapArchiveUploader.TimeoutErrorCode,
                "SAP did not answer within 300 s.", null, null, Array.Empty<SapErrorDetail>()))),
            CreateRequest());

        Assert.False(await op.Success);
        Assert.Contains("may already be in SAP", op.FailureMessage!);
        Assert.DoesNotContain("TaskCanceled", op.FailureMessage!);
    }

    [Fact]
    public async Task ATimedOutSignInSaysNothingWasUploaded()
    {
        var op = new UploadSapArchiveOperation();
        op.Start(new StubUploader(_ => Task.FromResult(
            new SapUploadResult(false, null, null, null, HttpSapArchiveUploader.SignInTimeoutErrorCode,
                "SAP did not answer the sign-in request within 30 s.", null, null,
                Array.Empty<SapErrorDetail>()))),
            CreateRequest());

        Assert.False(await op.Success);
        Assert.Contains("Nothing was uploaded", op.FailureMessage!);
    }

    /// <summary>
    /// Reads the bar a few times over about a second and a half of SAP "working".
    /// </summary>
    private static async Task<List<int>> SampleWhileWaiting(UploadSapArchiveOperation op)
    {
        var readings = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(400);
            readings.Add(op.Status.CurrentProgress);
        }
        return readings;
    }

    private static Func<IProgress<SapUploadProgress>?, Task<SapUploadResult>> WaitForSapThen(
        TaskCompletionSource<bool> gate, SapUploadResult result) =>
        async progress =>
        {
            progress?.Report(new SapUploadProgress(SapUploadStage.Uploading, 20));
            progress?.Report(new SapUploadProgress(SapUploadStage.WaitingForSap, 45));
            await gate.Task;
            return result;
        };

    private static SapUploadResult Success =>
        new(true, 201, "ABC", null, null, null, null, null, Array.Empty<SapErrorDetail>());

    private static SapUploadRequest CreateRequest() =>
        new(
            new SapConnectionConfig { Host = "https://sap.example.com", ServiceName = "ZARCHIVE_UPLOAD_SRV" },
            new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" },
            "BAR123",
            null,
            Encoding.UTF8.GetBytes("pdf"),
            "scan.pdf");

    private sealed class StubUploader : ISapArchiveUploader
    {
        private readonly Func<IProgress<SapUploadProgress>?, Task<SapUploadResult>> _upload;

        public StubUploader(Func<IProgress<SapUploadProgress>?, Task<SapUploadResult>> upload) => _upload = upload;

        public Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct) =>
            _upload(null);

        public Task<SapUploadResult> UploadAsync(SapUploadRequest request,
            IProgress<SapUploadProgress>? progress, CancellationToken ct) => _upload(progress);

        public Task<SapConnectionTestResult> TestConnectionAsync(SapConnectionConfig cfg, CancellationToken ct) =>
            Task.FromResult(new SapConnectionTestResult(true, "T", null));
    }
}
