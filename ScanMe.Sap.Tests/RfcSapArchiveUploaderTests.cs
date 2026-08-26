using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Sap;
using Xunit;

namespace NAPS2.Sap.Tests;

public class HttpSapArchiveUploaderTests
{
    [Fact]
    public async Task UploadAsync_ReturnsDocIdFromSuccessJson()
    {
        var uploader = CreateUploader((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? TokenResponse("TOKEN1")
                : JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}")));

        var result = await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(201, result.HttpStatusCode);
        Assert.Equal("ABC", result.ArchivDocId);
    }

    [Fact]
    public async Task UploadAsync_ReturnsAlternativeArchiveDocIdFromSuccessJson()
    {
        var uploader = CreateUploader((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? TokenResponse("TOKEN1")
                : JsonResponse(HttpStatusCode.Created, "{\"d\":{\"ArchiveDocId\":\"X\"}}")));

        var result = await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("X", result.ArchivDocId);
    }

    [Fact]
    public async Task UploadAsync_RefreshesTokenAndRetriesOnceForCsrfFailure()
    {
        var tokenFetches = 0;
        var postTokens = new List<string>();
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                tokenFetches++;
                return Task.FromResult(TokenResponse("TOKEN" + tokenFetches));
            }
            postTokens.Add(request.Headers.GetValues("x-csrf-token").Single());
            if (postTokens.Count == 1)
            {
                var response = JsonResponse(HttpStatusCode.Forbidden,
                    "{\"error\":{\"message\":{\"value\":\"CSRF token validation failed\"}}}");
                response.Headers.TryAddWithoutValidation("x-csrf-token", "Required");
                return Task.FromResult(response);
            }
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}"));
        });

        var result = await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, tokenFetches);
        Assert.Equal(new[] { "TOKEN1", "TOKEN2" }, postTokens);
    }

    [Fact]
    public async Task UploadAsync_ParsesSapErrorJson()
    {
        var errorJson = """
        {"error":{"code":"/IWBEP/CX_MGW_BUSI_EXCEPTION","message":{"value":"Upload rejected"},"innererror":{"transactionid":"TID123","errordetails":[{"code":"E1","message":"Detail message","severity":"error"}]}}}
        """;
        var uploader = CreateUploader((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get ? TokenResponse("TOKEN1") : JsonResponse(HttpStatusCode.BadRequest, errorJson)));

        var result = await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(400, result.HttpStatusCode);
        Assert.Equal("/IWBEP/CX_MGW_BUSI_EXCEPTION", result.ErrorCode);
        Assert.Equal("Upload rejected", result.ErrorMessage);
        Assert.Equal("TID123", result.TransactionId);
        var detail = Assert.Single(result.Details);
        Assert.Equal("E1", detail.Code);
        Assert.Equal("Detail message", detail.Message);
        Assert.Equal("error", detail.Severity);
        Assert.Equal(errorJson, result.RawResponseBody);
    }

    [Fact]
    public async Task UploadAsync_SendsExpectedHeadersIncludingLowercaseSlug()
    {
        HttpRequestMessage? post = null;
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(TokenResponse("TOKEN1"));
            }
            post = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, "{\"d\":{\"DocId\":\"ABC\"}}"));
        });

        await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.NotNull(post);
        Assert.Contains(post!.Headers, h => h.Key == "slug");
        Assert.DoesNotContain(post.Headers, h => h.Key == "Slug");
        AssertHeader(post, "x-sap-archivid", "PS");
        AssertHeader(post, "x-sap-barcode", "BAR123");
        AssertHeader(post, "x-sap-arobject", "BUS2012");
        AssertHeader(post, "x-sap-sapobj", "BUSOBJ");
        AssertHeader(post, "x-sap-objectid", "OBJ-BAR123");
    }

    [Fact]
    public void CreateHandler_DoesNotSetCertificateOverrideByDefault()
    {
        using var handler = HttpSapArchiveUploader.CreateHandler(new SapConnectionConfig { IgnoreCertificateErrors = false });

        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    /// <summary>
    /// The reverse of what this used to assert. Retrying a timed-out upload was measured to send the whole
    /// document three times, and a request that timed out was received in full as far as anyone here
    /// knows -- so those are up to three copies filed under one barcode, indistinguishable afterwards
    /// from a scan done three times, followed by a message saying the upload failed. A single honest
    /// failure is worth more than that, so the attempt is not repeated and the operator is told the
    /// document may already be in SAP.
    /// </summary>
    [Fact]
    public async Task UploadAsync_DoesNotSendTheDocumentAgainAfterATimeout()
    {
        var postAttempts = 0;
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(TokenResponse("TOKEN1"));
            }
            postAttempts++;
            throw new TaskCanceledException("timeout");
        });

        var result = await uploader.UploadAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, postAttempts);
        Assert.Equal(HttpSapArchiveUploader.TimeoutErrorCode, result.ErrorCode);
    }

    private static HttpSapArchiveUploader CreateUploader(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var connection = CreateConnection();
        return new HttpSapArchiveUploader(connection, new HttpClient(new DelegateHandler(handler)));
    }

    private static SapUploadRequest CreateRequest()
    {
        return new SapUploadRequest(
            CreateConnection(),
            new SapArchiveProfileSettings
            {
                EnableUpload = true,
                ArchiveId = "PS",
                ArObject = "BUS2012",
                SapObject = "BUSOBJ"
            },
            "BAR123",
            "OBJ-BAR123",
            Encoding.UTF8.GetBytes("pdf"),
            "scan.pdf");
    }

    private static SapConnectionConfig CreateConnection()
    {
        return new SapConnectionConfig
        {
            Host = "https://sap.example.com:44300",
            ServiceName = "ZARCHIVE_UPLOAD_SRV",
            Client = "100",
            Language = "DE",
            User = "SCANME"
        };
    }

    private static HttpResponseMessage TokenResponse(string token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("x-csrf-token", token);
        return response;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static void AssertHeader(HttpRequestMessage request, string name, string value)
    {
        Assert.True(request.Headers.TryGetValues(name, out var values), $"Missing header {name}");
        Assert.Equal(value, Assert.Single(values));
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
