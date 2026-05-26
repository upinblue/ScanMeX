using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// SAP ArchiveLink uploader using RFC calls.
/// </summary>
public class RfcSapArchiveUploader : ISapArchiveUploader
{
    private readonly ISapRfcClientFactory _rfcClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RfcSapArchiveUploader" /> class.
    /// </summary>
    /// <param name="rfcClientFactory">The RFC client factory. If omitted, SAP NCo is loaded at runtime.</param>
    public RfcSapArchiveUploader(ISapRfcClientFactory? rfcClientFactory = null)
    {
        _rfcClientFactory = rfcClientFactory ?? new NcoSapRfcClientFactory();
    }

    /// <inheritdoc />
    public Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct)
    {
        ValidateUploadRequest(request);
        ct.ThrowIfCancellationRequested();

        string? archivDocId = null;
        try
        {
            var client = _rfcClientFactory.Create(request.Connection);
            archivDocId = CreateArchiveObject(client, request, ct);
            InsertArchiveConnection(client, request, archivDocId, ct);
            Commit(client, ct);
            return Task.FromResult(new SapUploadResult(true, archivDocId, null, null));
        }
        catch (SapRfcException ex)
        {
            var message = archivDocId == null
                ? ex.Message
                : $"SAP RFC upload failed after content object creation. Content may remain on the content server with ARCHIV_DOC_ID {archivDocId}. {ex.Message}";
            return Task.FromResult(new SapUploadResult(false, archivDocId, message, ex.Key));
        }
    }

    /// <summary>
    /// Creates an ArchiveLink connection for a document that already exists on the content server.
    /// </summary>
    internal Task<SapUploadResult> InsertExistingArchiveDocumentAsync(SapUploadRequest request, string archivDocId, CancellationToken ct)
    {
        ValidateConnectionInsertRequest(request, archivDocId);
        ct.ThrowIfCancellationRequested();

        try
        {
            var client = _rfcClientFactory.Create(request.Connection);
            InsertArchiveConnection(client, request, archivDocId, ct);
            Commit(client, ct);
            return Task.FromResult(new SapUploadResult(true, archivDocId, null, null));
        }
        catch (SapRfcException ex)
        {
            return Task.FromResult(new SapUploadResult(false, archivDocId,
                $"SAP ArchiveLink connection insert failed for ARCHIV_DOC_ID {archivDocId}. {ex.Message}", ex.Key));
        }
    }

    /// <summary>
    /// Invokes a customer-specific RFC wrapper to create an ArchiveLink connection for an existing document.
    /// </summary>
    internal Task<SapUploadResult> InsertExistingArchiveDocumentWithCustomRfcAsync(SapUploadRequest request, string archivDocId, CancellationToken ct)
    {
        ValidateConnectionInsertRequest(request, archivDocId);
        if (string.IsNullOrWhiteSpace(request.Connection.CustomRfcName))
        {
            return Task.FromResult(new SapUploadResult(false, archivDocId, "CustomRfcName is required when ConnectionInsertMode is CustomRfc.", "CUSTOM_RFC_NAME_MISSING"));
        }
        ct.ThrowIfCancellationRequested();

        try
        {
            var client = _rfcClientFactory.Create(request.Connection);
            var function = client.CreateFunction(request.Connection.CustomRfcName!);
            SetArchiveConnectionParameters(function, request, archivDocId);
            function.SetValue("FILE_NAME", request.FileName);
            function.SetValue("MIME_TYPE", request.MimeType);
            function.SetValue("DESCRIPTION", request.Description);
            function.Invoke();
            Commit(client, ct);
            return Task.FromResult(new SapUploadResult(true, archivDocId, null, null));
        }
        catch (SapRfcException ex)
        {
            return Task.FromResult(new SapUploadResult(false, archivDocId,
                $"Custom SAP ArchiveLink connection insert failed for ARCHIV_DOC_ID {archivDocId}. {ex.Message}", ex.Key));
        }
    }

    /// <summary>
    /// Splits a byte array into raw chunks suitable for SAP RFC RAW table upload.
    /// </summary>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="chunkSize">The maximum chunk size. ArchiveLink uses 1024-byte RAW chunks.</param>
    /// <returns>Raw chunks. Data is not base64-encoded.</returns>
    public static byte[][] SplitIntoRawChunks(byte[] bytes, int chunkSize = 1024)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }
        if (bytes.Length == 0)
        {
            return Array.Empty<byte[]>();
        }

        var chunks = new List<byte[]>((bytes.Length + chunkSize - 1) / chunkSize);
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(bytes, offset, chunk, 0, length);
            chunks.Add(chunk);
        }
        return chunks.ToArray();
    }

    private static string CreateArchiveObject(ISapRfcClient client, SapUploadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var function = client.CreateFunction("ARCHIVOBJECT_CREATE_TABLE");
        function.SetValue("ARCHIV_ID", request.Profile.ArchiveId);
        function.SetValue("DOCUMENT_TYPE", request.Profile.ArDocType);
        function.SetValue("LENGTH", request.DocumentBytes.Length);

        var table = function.GetTable("BINARCHIVOBJECT");
        foreach (var chunk in SplitIntoRawChunks(request.DocumentBytes))
        {
            ct.ThrowIfCancellationRequested();
            table.Append();
            table.SetValue("LINE", chunk);
        }

        function.Invoke();
        var archivDocId = function.GetString("ARCHIV_DOC_ID");
        if (string.IsNullOrWhiteSpace(archivDocId))
        {
            throw new SapRfcException("ARCHIV_DOC_ID_MISSING", "ARCHIVOBJECT_CREATE_TABLE did not return ARCHIV_DOC_ID.");
        }
        return archivDocId!.Trim();
    }

    private static void InsertArchiveConnection(ISapRfcClient client, SapUploadRequest request, string archivDocId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var function = client.CreateFunction("ARCHIV_CONNECTION_INSERT");
        SetArchiveConnectionParameters(function, request, archivDocId);
        function.Invoke();
    }

    private static void SetArchiveConnectionParameters(ISapRfcFunction function, SapUploadRequest request, string archivDocId)
    {
        function.SetValue("ARCHIV_DOC_ID", archivDocId);
        function.SetValue("ARCHIV_ID", request.Profile.ArchiveId);
        function.SetValue("AR_OBJECT", request.Profile.ArDocType);
        function.SetValue("OBJECT_ID", request.ObjectKey);
        function.SetValue("SAP_OBJECT", request.Profile.SapObjectType);
        function.SetValue("DOC_TYPE", GetSapDocType(request));
    }

    private static void Commit(ISapRfcClient client, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var function = client.CreateFunction("BAPI_TRANSACTION_COMMIT");
        function.SetValue("WAIT", "X");
        function.Invoke();
    }

    private static string GetSapDocType(SapUploadRequest request)
    {
        if (string.Equals(request.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(request.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "PDF";
        }
        return string.IsNullOrWhiteSpace(request.MimeType) ? "BIN" : request.MimeType;
    }

    private static void ValidateUploadRequest(SapUploadRequest request)
    {
        ValidateConnectionInsertRequest(request, "pending");
        if (request.DocumentBytes == null)
        {
            throw new ArgumentException("DocumentBytes is required.", nameof(request));
        }
    }

    private static void ValidateConnectionInsertRequest(SapUploadRequest request, string archivDocId)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (request.Connection == null)
        {
            throw new ArgumentException("Connection is required.", nameof(request));
        }
        if (request.Profile == null)
        {
            throw new ArgumentException("Profile is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ObjectKey))
        {
            throw new ArgumentException("ObjectKey is required for SAP ArchiveLink upload.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.ArchiveId))
        {
            throw new ArgumentException("Profile.ArchiveId is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.ArDocType))
        {
            throw new ArgumentException("Profile.ArDocType is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.SapObjectType))
        {
            throw new ArgumentException("Profile.SapObjectType is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(archivDocId))
        {
            throw new ArgumentException("ARCHIV_DOC_ID is required.", nameof(archivDocId));
        }
    }
}
