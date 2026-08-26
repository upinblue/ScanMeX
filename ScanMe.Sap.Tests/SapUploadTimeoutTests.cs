using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// What an upload does when SAP is slow. The 60 second window this replaced covered sending the document
/// and SAP archiving it together, was not configurable, and -- because it came from
/// <see cref="HttpClient.Timeout" /> -- arrived as a <see cref="TaskCanceledException" />, which is what
/// the operator pressing Cancel also looks like. Both halves of that were wrong in ways that reached the
/// archive.
/// </summary>
public class SapUploadTimeoutTests
{
    /// <summary>
    /// The short deadline the timeout tests wait out. It is the shortest a connection will accept, which
    /// is what keeps these tests to seconds rather than minutes.
    /// </summary>
    private const int ShortTimeoutSeconds = 5;

    /// <summary>
    /// The one that matters. A timeout means the request was received and the answer is late, so trying
    /// again files the same document a second and a third time -- measured before this change: three
    /// complete uploads of a 2 MB document, and then a failure message for a document that was by then in
    /// SAP three times over.
    /// </summary>
    [Fact]
    public async Task ATimedOutUploadIsNotSentAgain()
    {
        var uploads = 0;
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            Interlocked.Increment(ref uploads);
            await request.Content!.CopyToAsync(Stream.Null);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}");
        });

        var result = await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, uploads);
    }

    [Fact]
    public async Task ATimedOutUploadIsReportedAsATimeout()
    {
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            await request.Content!.CopyToAsync(Stream.Null);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{}");
        });

        var result = await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);

        Assert.Equal(HttpSapArchiveUploader.TimeoutErrorCode, result.ErrorCode);
    }

    /// <summary>
    /// A timeout while fetching the CSRF token used to escape as a <see cref="TaskCanceledException" />,
    /// which derives from <see cref="OperationCanceledException" /> -- so it went straight past the
    /// uploader's handlers and the caller reported it to the operator as "upload cancelled", for a
    /// gateway that had simply not answered.
    /// </summary>
    [Fact]
    public async Task ATimedOutSignInIsReportedRatherThanLookingLikeACancellation()
    {
        var uploader = CreateUploader(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return TokenResponse("T");
        });

        var result = await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(HttpSapArchiveUploader.SignInTimeoutErrorCode, result.ErrorCode);
    }

    /// <summary>
    /// The other half of telling the two apart: the operator's own Cancel still has to come out as one.
    /// </summary>
    [Fact]
    public async Task CancellingStillCancels()
    {
        using var cts = new CancellationTokenSource();
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            await request.Content!.CopyToAsync(Stream.Null);
            cts.Cancel();
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            uploader.UploadAsync(ShortTimeoutRequest(), null, cts.Token));
    }

    /// <summary>
    /// A connection that never came up carried nothing, so sending again cannot duplicate anything. That
    /// retry stays.
    /// </summary>
    [Fact]
    public async Task AConnectionFailureIsStillRetried()
    {
        var uploads = 0;
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(TokenResponse("T"));
            }
            Interlocked.Increment(ref uploads);
            throw new HttpRequestException("No such host is known.");
        });

        var result = await uploader.UploadAsync(CreateRequest(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, uploads);
    }

    [Fact]
    public async Task TheDeadlineComesFromTheConnection()
    {
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            await request.Content!.CopyToAsync(Stream.Null);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{}");
        });

        var watch = Stopwatch.StartNew();
        await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);
        watch.Stop();

        // Comfortably inside HttpClient's own 100 second default, and inside the 60 the uploader used to
        // impose -- so it can only be the connection's own value that ended it.
        Assert.InRange(watch.Elapsed.TotalSeconds, ShortTimeoutSeconds - 1, ShortTimeoutSeconds + 15);
    }

    /// <summary>
    /// Which side of the transfer ran out of time is the whole diagnosis: still sending means the link is
    /// too slow for the document, sent and waiting means SAP is the slow part -- and that the document
    /// may already be filed. Nothing said either before, because the HTTP half of an upload logged
    /// nothing at all.
    /// </summary>
    [Fact]
    public async Task TheLogSaysTheDocumentWasFullySentBeforeSapWentQuiet()
    {
        var lines = new List<string>();
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            await request.Content!.CopyToAsync(Stream.Null);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{}");
        });
        uploader.DiagnosticLog = lines.Add;

        await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);

        var timeout = Assert.Single(lines, x => x.StartsWith("Timed out", StringComparison.Ordinal));
        Assert.Contains("was sent in full", timeout, StringComparison.Ordinal);
        Assert.Contains("may already be archived", timeout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLogSaysTheDocumentWasStillGoingOutWhenTheDeadlineExpired()
    {
        var lines = new List<string>();
        var uploader = CreateUploader(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TokenResponse("T");
            }
            // Never pulls the body, which is what a stalled link looks like from here.
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return JsonResponse(HttpStatusCode.Created, "{}");
        });
        uploader.DiagnosticLog = lines.Add;

        await uploader.UploadAsync(ShortTimeoutRequest(), null, CancellationToken.None);

        var timeout = Assert.Single(lines, x => x.StartsWith("Timed out", StringComparison.Ordinal));
        Assert.Contains("while still sending", timeout, StringComparison.Ordinal);
        Assert.Contains("Nothing was archived", timeout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every step of the chain has to say what it did, including the ones that succeed quietly.
    /// </summary>
    [Fact]
    public async Task ASuccessfulUploadNamesEachStepAndHowLongItTook()
    {
        var lines = new List<string>();
        var uploader = CreateUploader(Ok);
        uploader.DiagnosticLog = lines.Add;

        await uploader.UploadAsync(CreateRequest(new byte[128 * 1024]), null, CancellationToken.None);

        Assert.Contains(lines, x => x.Contains("started", StringComparison.Ordinal) &&
                                    x.Contains("upload timeout", StringComparison.Ordinal));
        Assert.Contains(lines, x => x.StartsWith("Signed in to SAP after", StringComparison.Ordinal));
        Assert.Contains(lines, x => x.Contains("SAP answered HTTP 201", StringComparison.Ordinal) &&
                                    x.Contains("waiting for SAP", StringComparison.Ordinal));
        Assert.Contains(lines, x => x.Contains("archived as ArchivDocId 'ABC'", StringComparison.Ordinal));
    }

    private static Task<HttpResponseMessage> Ok(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(TokenResponse("TOKEN1"));
        }
        return request.Content!.CopyToAsync(Stream.Null)
            .ContinueWith(_ => JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}"), ct);
    }

    private static HttpSapArchiveUploader CreateUploader(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(CreateConnection(), new HttpClient(new DelegateHandler(handler)));

    private static SapUploadRequest ShortTimeoutRequest() =>
        CreateRequest(new byte[64 * 1024], CreateConnection(ShortTimeoutSeconds));

    private static SapUploadRequest CreateRequest(byte[]? documentBytes = null,
        SapConnectionConfig? connection = null) =>
        new(
            connection ?? CreateConnection(),
            new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" },
            "BAR123",
            null,
            documentBytes ?? Encoding.UTF8.GetBytes("pdf"),
            "scan.pdf");

    private static SapConnectionConfig CreateConnection(int timeoutSeconds = 0) => new()
    {
        Host = "https://sap.example.com:44300",
        ServiceName = "ZARCHIVE_UPLOAD_SRV",
        Client = "100",
        Language = "DE",
        User = "SCANME",
        ConnectTimeoutSeconds = timeoutSeconds,
        UploadTimeoutSeconds = timeoutSeconds
    };

    private static HttpResponseMessage TokenResponse(string token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("x-csrf-token", token);
        return response;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            _handler(request, ct);
    }
}
