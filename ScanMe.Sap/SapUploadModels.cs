using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// The step an ArchiveLink upload is on. Reported so the progress window can name what is happening
/// instead of showing a bar that sits still: fetching the CSRF token and waiting for SAP to accept the
/// document are the two steps that take noticeable time, and they look identical from the outside.
/// </summary>
public enum SapUploadStage
{
    /// <summary>Reading the document and building the request.</summary>
    Preparing,

    /// <summary>Fetching a CSRF token from SAP Gateway.</summary>
    Authenticating,

    /// <summary>Sending the document's bytes.</summary>
    Uploading,

    /// <summary>The document has been sent; SAP is processing it.</summary>
    WaitingForSap,

    /// <summary>The attempt failed and is being made again.</summary>
    Retrying
}

/// <summary>
/// How far an ArchiveLink upload has got.
/// </summary>
/// <param name="Stage">The step being performed.</param>
/// <param name="Percent">Overall completion from 0 to 100.</param>
public record SapUploadProgress(SapUploadStage Stage, int Percent);

/// <summary>
/// Uploads a document to a customer-specific SAP ArchiveLink OData service.
/// </summary>
public interface ISapArchiveUploader
{
    /// <summary>
    /// Uploads the document to the SAP OData AttachmentSet endpoint.
    /// </summary>
    /// <param name="request">The upload request.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The upload result.</returns>
    Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct);

    /// <summary>
    /// Uploads the document, reporting each step so the caller can show progress.
    /// </summary>
    /// <param name="request">The upload request.</param>
    /// <param name="progress">Receives the upload's stage and overall percentage, or null for no reporting.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The upload result.</returns>
    Task<SapUploadResult> UploadAsync(SapUploadRequest request, IProgress<SapUploadProgress>? progress,
        CancellationToken ct);

    /// <summary>
    /// Tests connectivity by fetching a CSRF token for the supplied connection.
    /// </summary>
    /// <param name="cfg">The SAP OData connection configuration.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The connection test result.</returns>
    Task<SapConnectionTestResult> TestConnectionAsync(SapConnectionConfig cfg, CancellationToken ct);
}

/// <summary>
/// Describes a SAP ArchiveLink OData upload request.
/// </summary>
/// <param name="Connection">The SAP OData connection configuration.</param>
/// <param name="Profile">The ArchiveLink profile settings.</param>
/// <param name="Barcode">The already resolved barcode value sent as <c>x-sap-barcode</c>.</param>
/// <param name="ObjectId">The already resolved SAP object id sent as <c>x-sap-objectid</c>.</param>
/// <param name="DocumentBytes">The raw document bytes.</param>
/// <param name="FileName">The already resolved original file name sent as <c>slug</c>.</param>
/// <param name="OverrideMimeType">An optional MIME type override.</param>
public record SapUploadRequest(
    SapConnectionConfig Connection,
    SapArchiveProfileSettings Profile,
    string Barcode,
    string? ObjectId,
    byte[] DocumentBytes,
    string FileName,
    string? OverrideMimeType = null)
{
    /// <summary>
    /// Legacy constructor retained for older integration code. The old object key is used as OData barcode.
    /// </summary>
    public SapUploadRequest(SapConnectionConfig connection, SapArchiveProfileSettings profile, string objectKey,
        byte[] documentBytes, string fileName, string mimeType, string description)
        : this(connection, profile, objectKey, null, documentBytes, fileName, mimeType)
    {
    }
};

/// <summary>
/// Describes the outcome of a SAP ArchiveLink OData upload.
/// </summary>
public record SapUploadResult(
    bool Success,
    int? HttpStatusCode,
    string? ArchivDocId,
    string? LocationHeader,
    string? ErrorCode,
    string? ErrorMessage,
    string? TransactionId,
    string? RawResponseBody,
    IReadOnlyList<SapErrorDetail> Details);

/// <summary>
/// Describes one SAP Gateway error detail entry.
/// </summary>
/// <param name="Code">The SAP error code.</param>
/// <param name="Message">The SAP error message.</param>
/// <param name="Severity">The SAP error severity.</param>
public record SapErrorDetail(string Code, string Message, string Severity);

/// <summary>
/// Describes the outcome of a SAP OData connection test.
/// </summary>
/// <param name="Success">A value indicating whether a CSRF token was obtained.</param>
/// <param name="CsrfToken">The fetched CSRF token.</param>
/// <param name="ErrorMessage">The error message when the test failed.</param>
public record SapConnectionTestResult(bool Success, string? CsrfToken, string? ErrorMessage);
