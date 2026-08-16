#nullable enable
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using NAPS2.Scan;
using NAPS2.SharePoint;
using Xunit;

namespace NAPS2.Lib.Tests.SharePoint;

/// <summary>
/// Drives the whole upload against a stand-in for Microsoft Graph: token, site lookup, library lookup and
/// the PUT that writes the file. The failure the customer hit was in the last request only, which the
/// unit-level URL tests now pin down; these check that the requests before it still line up and that a
/// Graph error is reported with its message rather than as a bare status code.
/// </summary>
public class SharePointUploadServiceTests
{
    [Fact]
    public async Task UploadFileAsync_PutsIntoTheConfiguredSubfolder()
    {
        var graph = new FakeGraph();
        var file = CreateTempPdf();
        try
        {
            await CreateService(graph).UploadFileAsync(
                Settings(folderPath: "Robin_Test"), file, "Test_1.pdf", null, CancellationToken.None);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/sites/SITEID/drives/DRIVEID/root:/Robin_Test/Test_1.pdf:/content",
            graph.PutUrl);
    }

    [Fact]
    public async Task UploadFileAsync_PutsIntoTheLibraryRootWhenNoFolderIsConfigured()
    {
        var graph = new FakeGraph();
        var file = CreateTempPdf();
        try
        {
            await CreateService(graph).UploadFileAsync(
                Settings(), file, "Test_1.pdf", null, CancellationToken.None);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal(
            "https://graph.microsoft.com/v1.0/sites/SITEID/drives/DRIVEID/root:/Test_1.pdf:/content",
            graph.PutUrl);
    }

    [Fact]
    public async Task UploadFileAsync_CombinesASubpathOnTheLibraryWithTheFolder()
    {
        var graph = new FakeGraph();
        var file = CreateTempPdf();
        try
        {
            await CreateService(graph).UploadFileAsync(
                Settings(library: "Dokumente/Eingang", folderPath: "Robin_Test"), file, "Test_1.pdf", null,
                CancellationToken.None);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.EndsWith("/root:/Eingang/Robin_Test/Test_1.pdf:/content", graph.PutUrl);
    }

    [Fact]
    public async Task UploadFileAsync_ReportsProgressUpTo100()
    {
        var graph = new FakeGraph();
        var reported = new List<int>();
        var file = CreateTempPdf();
        try
        {
            await CreateService(graph).UploadFileAsync(
                Settings(), file, "Test_1.pdf", new SynchronousProgress(reported), CancellationToken.None);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal(0, reported[0]);
        Assert.Equal(100, reported[^1]);
        // Never goes backwards: the operator reads the bar as "how far along is this document".
        Assert.Equal(reported.OrderBy(x => x), reported);
    }

    [Fact]
    public async Task UploadFileAsync_SurfacesTheGraphErrorMessageWhenThePutFails()
    {
        var graph = new FakeGraph
        {
            PutResponse = () => Json(HttpStatusCode.BadRequest,
                """{"error":{"code":"invalidRequest","message":"The provided path does not exist."}}""")
        };
        var file = CreateTempPdf();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(graph).UploadFileAsync(
                    Settings(folderPath: "Robin_Test"), file, "Test_1.pdf", null, CancellationToken.None));

            Assert.Contains("invalidRequest", ex.Message);
            Assert.Contains("The provided path does not exist.", ex.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task UploadFileAsync_NamesTheMissingSettingsRatherThanFailingAtTheEndpoint()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(new FakeGraph()).UploadFileAsync(
                new SharePointUploadSettings { SiteUrl = "https://contoso.sharepoint.com/sites/Scans" },
                "irrelevant.pdf", "Test_1.pdf", null, CancellationToken.None));

        Assert.Contains(nameof(SharePointUploadSettings.LibraryNameOrPath), ex.Message);
        Assert.Contains(nameof(SharePointUploadSettings.TenantId), ex.Message);
        Assert.Contains(nameof(SharePointUploadSettings.ClientId), ex.Message);
        Assert.Contains(nameof(SharePointUploadSettings.ClientSecret), ex.Message);
    }

    private static SharePointUploadService CreateService(FakeGraph graph) =>
        new(new HttpClient(graph));

    private static SharePointUploadSettings Settings(string library = "Dokumente", string? folderPath = null) =>
        new()
        {
            SiteUrl = "https://contoso.sharepoint.com/sites/Scans",
            LibraryNameOrPath = library,
            FolderPath = folderPath,
            TenantId = "11111111-1111-1111-1111-111111111111",
            ClientId = "22222222-2222-2222-2222-222222222222",
            ClientSecret = "secret"
        };

    private static string CreateTempPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(new string('x', 200_000)));
        return path;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Answers the four requests an upload makes, in the shape Graph uses.
    /// </summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public string? PutUrl { get; private set; }

        public Func<HttpResponseMessage>? PutResponse { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Put)
            {
                PutUrl = url;
                return Task.FromResult(PutResponse?.Invoke() ??
                                       Json(HttpStatusCode.Created,
                                           """{"webUrl":"https://contoso.sharepoint.com/x.pdf"}"""));
            }
            if (url.Contains("login.microsoftonline.com"))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"TOKEN"}"""));
            }
            if (url.EndsWith("/drives"))
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"value":[{"id":"DRIVEID","name":"Dokumente","webUrl":"https://contoso.sharepoint.com/sites/Scans/Dokumente"}]}"""));
            }
            return Task.FromResult(Json(HttpStatusCode.OK, """{"id":"SITEID"}"""));
        }
    }

    /// <summary>
    /// <see cref="Progress{T}"/> posts to the synchronization context, so the values can arrive after the
    /// upload returned. The operation wrappers report on whatever thread calls them, so recording inline
    /// is both closer to production and deterministic here.
    /// </summary>
    private sealed class SynchronousProgress : IProgress<int>
    {
        private readonly List<int> _values;

        public SynchronousProgress(List<int> values) => _values = values;

        public void Report(int value)
        {
            lock (_values)
            {
                _values.Add(value);
            }
        }
    }
}
