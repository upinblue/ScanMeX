using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// Uploads a document to SAP ArchiveLink and links it to a SAP business object.
/// </summary>
public interface ISapArchiveUploader
{
    /// <summary>
    /// Uploads the document and creates the SAP ArchiveLink connection.
    /// </summary>
    /// <param name="request">The upload request.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The upload result.</returns>
    Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct);
}

/// <summary>
/// Describes a SAP ArchiveLink upload request.
/// </summary>
/// <param name="Connection">The SAP connection configuration.</param>
/// <param name="Profile">The ArchiveLink profile settings.</param>
/// <param name="ObjectKey">The SAP business object key.</param>
/// <param name="DocumentBytes">The raw document bytes to archive. These bytes are never base64-encoded by the uploader.</param>
/// <param name="FileName">The original file name.</param>
/// <param name="MimeType">The document MIME type.</param>
/// <param name="Description">The ArchiveLink description.</param>
public record SapUploadRequest(
    SapConnectionConfig Connection,
    SapArchiveProfileSettings Profile,
    string ObjectKey,
    byte[] DocumentBytes,
    string FileName,
    string MimeType,
    string Description);

/// <summary>
/// Describes the outcome of a SAP ArchiveLink upload.
/// </summary>
/// <param name="Success">A value indicating whether the upload and connection insert succeeded.</param>
/// <param name="ArchivDocId">The 32-character ArchiveLink document ID returned by the content server/SAP.</param>
/// <param name="ErrorMessage">The user-readable error message, if any.</param>
/// <param name="ErrorCode">The technical error code, if any.</param>
public record SapUploadResult(bool Success, string? ArchivDocId, string? ErrorMessage, string? ErrorCode);
