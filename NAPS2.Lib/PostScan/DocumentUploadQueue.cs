namespace NAPS2.PostScan;

/// <summary>
/// Holds documents that have been scanned and saved but not yet archived: everything waiting for the
/// manual upload button, plus documents whose automatic upload failed, so a target system that was
/// unreachable at scan time can be retried instead of leaving the document silently unarchived.
/// </summary>
public class DocumentUploadQueue
{
    private readonly List<PendingDocument> _documents = [];

    public event EventHandler? Changed;

    public IReadOnlyList<PendingDocument> Documents
    {
        get
        {
            lock (_documents)
            {
                return _documents.ToList();
            }
        }
    }

    public bool HasPending => Documents.Any(x => x.Status is DocumentUploadStatus.Pending or DocumentUploadStatus.Failed);

    public void Add(PendingDocument document)
    {
        lock (_documents)
        {
            _documents.Add(document);
        }
        OnChanged();
    }

    public void Remove(PendingDocument document)
    {
        lock (_documents)
        {
            _documents.Remove(document);
        }
        OnChanged();
    }

    /// <summary>
    /// Drops every document that was uploaded successfully, leaving pending and failed ones so a failed
    /// upload can be retried instead of being silently lost.
    /// </summary>
    public void RemoveUploaded()
    {
        lock (_documents)
        {
            _documents.RemoveAll(x => x.Status == DocumentUploadStatus.Uploaded);
        }
        OnChanged();
    }

    public void Clear()
    {
        lock (_documents)
        {
            _documents.Clear();
        }
        OnChanged();
    }

    public void NotifyChanged() => OnChanged();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
