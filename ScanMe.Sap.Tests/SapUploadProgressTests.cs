using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NAPS2.Sap.Tests;

/// <summary>
/// What the progress window is told while a document goes to SAP. The bar used to jump to 20% and sit
/// there for the whole upload, which is indistinguishable from an upload that has hung -- and the
/// SharePoint upload right next to it reported real progress the whole way.
/// </summary>
public class SapUploadProgressTests
{
    [Fact]
    public async Task UploadAsync_ReportsEveryStageInOrder()
    {
        var reported = new List<SapUploadProgress>();
        var uploader = CreateUploader(Ok);

        await uploader.UploadAsync(CreateRequest(), new RecordingProgress(reported), CancellationToken.None);

        var stages = reported.Select(x => x.Stage).Distinct().ToList();
        Assert.Equal(
            new[]
            {
                SapUploadStage.Preparing, SapUploadStage.Authenticating, SapUploadStage.Uploading,
                SapUploadStage.WaitingForSap
            },
            stages);
    }

    [Fact]
    public async Task UploadAsync_ProgressNeverGoesBackwardsOnASuccessfulUpload()
    {
        var reported = new List<SapUploadProgress>();
        var uploader = CreateUploader(Ok);

        await uploader.UploadAsync(CreateRequest(), new RecordingProgress(reported), CancellationToken.None);

        var percentages = reported.Select(x => x.Percent).ToList();
        Assert.Equal(percentages.OrderBy(x => x), percentages);
        Assert.Equal(0, percentages[0]);
    }

    /// <summary>
    /// The document's bytes have to move the bar. A single ByteArrayContent hands everything to the socket
    /// in one call, so the transfer of a large scan showed as nothing at all.
    /// </summary>
    [Fact]
    public async Task UploadAsync_ReportsSeveralStepsWhileSendingALargeDocument()
    {
        var reported = new List<SapUploadProgress>();
        var uploader = CreateUploader(Ok);

        await uploader.UploadAsync(CreateRequest(documentBytes: new byte[512 * 1024]),
            new RecordingProgress(reported), CancellationToken.None);

        var uploading = reported.Where(x => x.Stage == SapUploadStage.Uploading).Select(x => x.Percent).ToList();
        Assert.True(uploading.Count >= 4, $"Expected several upload steps, got {uploading.Count}");
        Assert.True(uploading.Max() <= 90, "The bar must not reach the end before SAP has answered.");
    }

    [Fact]
    public async Task UploadAsync_ReportsARetryRatherThanStalling()
    {
        var reported = new List<SapUploadProgress>();
        var attempts = 0;
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(TokenResponse("TOKEN"));
            }
            if (++attempts == 1)
            {
                throw new HttpRequestException("connection reset");
            }
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}"));
        });

        var result = await uploader.UploadAsync(CreateRequest(), new RecordingProgress(reported),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(reported, x => x.Stage == SapUploadStage.Retrying);
    }

    [Fact]
    public async Task UploadAsync_WithoutAProgressCallbackStillUploads()
    {
        var uploader = CreateUploader(Ok);

        var result = await uploader.UploadAsync(CreateRequest(), null, CancellationToken.None);

        Assert.True(result.Success);
    }

    /// <summary>
    /// One uploader is created per document, so the HttpClient it makes for itself has to go away with it.
    /// A supplied client belongs to the caller and must survive.
    /// </summary>
    [Fact]
    public void Dispose_LeavesACallerSuppliedHttpClientAlone()
    {
        var httpClient = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(TokenResponse("T"))));
        using (var uploader = new HttpSapArchiveUploader(CreateConnection(), httpClient))
        {
            Assert.NotNull(uploader);
        }

        // Throws ObjectDisposedException if the uploader disposed a client it does not own.
        httpClient.CancelPendingRequests();
    }

    [Fact]
    public void Dispose_IsSafeToCallTwice()
    {
        var uploader = new HttpSapArchiveUploader(CreateConnection());
        uploader.Dispose();
        uploader.Dispose();
    }

    private static Task<HttpResponseMessage> Ok(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(request.Method == HttpMethod.Get
            ? TokenResponse("TOKEN1")
            : JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}"));

    private static HttpSapArchiveUploader CreateUploader(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(CreateConnection(), new HttpClient(new DelegateHandler(handler)));

    private static SapUploadRequest CreateRequest(byte[]? documentBytes = null) =>
        new(
            CreateConnection(),
            new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" },
            "BAR123",
            null,
            documentBytes ?? Encoding.UTF8.GetBytes("pdf"),
            "scan.pdf");

    private static SapConnectionConfig CreateConnection() => new()
    {
        Host = "https://sap.example.com:44300",
        ServiceName = "ZARCHIVE_UPLOAD_SRV",
        Client = "100",
        Language = "DE",
        User = "SCANME"
    };

    private static HttpResponseMessage TokenResponse(string token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("x-csrf-token", token);
        return response;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingProgress : IProgress<SapUploadProgress>
    {
        private readonly List<SapUploadProgress> _values;

        public RecordingProgress(List<SapUploadProgress> values) => _values = values;

        public void Report(SapUploadProgress value) => _values.Add(value);
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // A real transport pulls the body through HttpContent.SerializeToStreamAsync, which is where
            // the upload progress comes from. A handler that answers without reading it would make the
            // streaming look untested.
            if (request.Content != null)
            {
                await request.Content.CopyToAsync(Stream.Null);
            }
            return await _handler(request, cancellationToken);
        }
    }
}
