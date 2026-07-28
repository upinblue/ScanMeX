using NAPS2.Scan;

namespace NAPS2.PostScan;

public enum DocumentUploadStatus
{
    Pending,
    Uploading,
    Uploaded,
    Failed
}

/// <summary>
/// A saved document waiting to be uploaded, or already uploaded. Documents live here between the scan
/// finishing and the upload completing, which is what makes the manual upload button possible.
/// </summary>
public sealed class PendingDocument
{
    public Guid Id { get; } = Guid.NewGuid();

    public required ScanProfile Profile { get; init; }

    public required ScanContext Context { get; set; }

    public required string FilePath { get; set; }

    public string FileName => Path.GetFileName(FilePath);

    public int PageCount => Context.Images.Count;

    /// <summary>
    /// Whether the saved file is only a staging copy that should be removed once the upload succeeded.
    /// </summary>
    public bool DeleteFileAfterUpload { get; init; }

    public DocumentUploadStatus Status { get; set; } = DocumentUploadStatus.Pending;

    public string? Message { get; set; }
}
