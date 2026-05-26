using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Sap;
using NSubstitute;
using Xunit;

namespace NAPS2.Sap.Tests;

public class RfcSapArchiveUploaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2500)]
    public void SplitIntoRawChunks_UsesRaw1024ByteChunks(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(x => (byte)(x % 251)).ToArray();

        var chunks = RfcSapArchiveUploader.SplitIntoRawChunks(bytes);

        Assert.Equal((length + 1023) / 1024, chunks.Length);
        Assert.All(chunks.Take(Math.Max(0, chunks.Length - 1)), chunk => Assert.Equal(1024, chunk.Length));
        if (length > 0)
        {
            Assert.Equal(((length - 1) % 1024) + 1, chunks[^1].Length);
        }
        Assert.Equal(bytes, chunks.SelectMany(x => x).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadAsync_ThrowsForEmptyObjectKeyBeforeRfcCall(string? objectKey)
    {
        var factory = Substitute.For<ISapRfcClientFactory>();
        var uploader = new RfcSapArchiveUploader(factory);
        var request = CreateRequest(objectKey ?? string.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => uploader.UploadAsync(request, CancellationToken.None));
        factory.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task UploadAsync_PassesArchivDocIdFromCreateTableToConnectionInsert()
    {
        const string archivDocId = "0123456789ABCDEF0123456789ABCDEF";
        var factory = Substitute.For<ISapRfcClientFactory>();
        var client = Substitute.For<ISapRfcClient>();
        var createFunction = Substitute.For<ISapRfcFunction>();
        var insertFunction = Substitute.For<ISapRfcFunction>();
        var commitFunction = Substitute.For<ISapRfcFunction>();
        var binArchiveObject = Substitute.For<ISapRfcTable>();

        factory.Create(Arg.Any<SapConnectionConfig>()).Returns(client);
        client.CreateFunction("ARCHIVOBJECT_CREATE_TABLE").Returns(createFunction);
        client.CreateFunction("ARCHIV_CONNECTION_INSERT").Returns(insertFunction);
        client.CreateFunction("BAPI_TRANSACTION_COMMIT").Returns(commitFunction);
        createFunction.GetTable("BINARCHIVOBJECT").Returns(binArchiveObject);
        createFunction.GetString("ARCHIV_DOC_ID").Returns(archivDocId);

        var uploader = new RfcSapArchiveUploader(factory);

        var result = await uploader.UploadAsync(CreateRequest("4500001234"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(archivDocId, result.ArchivDocId);
        insertFunction.Received(1).SetValue("ARCHIV_DOC_ID", archivDocId);
        insertFunction.Received(1).SetValue("OBJECT_ID", "4500001234");
        insertFunction.Received(1).Invoke();
        commitFunction.Received(1).SetValue("WAIT", "X");
        commitFunction.Received(1).Invoke();
    }

    private static SapUploadRequest CreateRequest(string objectKey)
    {
        return new SapUploadRequest(
            new SapConnectionConfig
            {
                ConnectionMode = ConnectionMode.Rfc,
                SystemId = "TST",
                AppServerHost = "sap.example.local",
                SystemNumber = "00",
                Client = "100",
                Language = "EN",
                User = "SCANME"
            },
            new SapArchiveProfileSettings
            {
                EnableUpload = true,
                ArchiveId = "A1",
                SapObjectType = "BUS2012",
                ArDocType = "ZSCAN_PDF"
            },
            objectKey,
            new byte[] { 1, 2, 3 },
            "scan.pdf",
            "application/pdf",
            "ScanMe test upload");
    }
}
