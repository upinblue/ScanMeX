namespace NAPS2.PostScan;

/// <summary>
/// Every document the current session has produced, in scan order: the ones waiting for the upload
/// button, the ones missing an identifier, the ones whose upload failed, and the ones that are finished.
/// </summary>
/// <remarks>
/// The finished ones are kept on purpose. This used to hold only what was still outstanding, so a
/// document that uploaded successfully vanished without trace and the only evidence it had been archived
/// was a notification that scrolled away. Keeping them is what lets the document list answer "did that
/// one go through?" at a glance, which is the question operators actually ask.
/// </remarks>
public class DocumentQueue
{
    private readonly List<ScannedDocument> _documents = [];

    public event EventHandler? Changed;

    public IReadOnlyList<ScannedDocument> Documents
    {
        get
        {
            lock (_documents)
            {
                return _documents.ToList();
            }
        }
    }

    /// <summary>
    /// Documents that still have somewhere to go: waiting, failed, or missing an identifier.
    /// </summary>
    public IReadOnlyList<ScannedDocument> Outstanding => Documents
        .Where(x => x.Status is DocumentStatus.Pending or DocumentStatus.Failed
            or DocumentStatus.NeedsIdentifier)
        .ToList();

    /// <summary>
    /// Documents the upload button can act on right now. A document missing a required identifier is
    /// deliberately not one of them -- it is outstanding, but pressing upload must not file it.
    /// </summary>
    public IReadOnlyList<ScannedDocument> ReadyToUpload => Documents
        .Where(x => x.Status is DocumentStatus.Pending or DocumentStatus.Failed)
        .Where(x => x.HasEverythingItNeeds())
        .ToList();

    public bool HasOutstanding => Outstanding.Count > 0;

    public bool HasReadyToUpload => ReadyToUpload.Count > 0;

    public void Add(ScannedDocument document)
    {
        lock (_documents)
        {
            _documents.Add(document);
        }
        OnChanged();
    }

    /// <summary>
    /// Puts a document straight after another one, which is where a document split off one belongs: the
    /// list reads in the order the pages are in, and appending would put the second half of a document
    /// at the bottom, past everything scanned after it.
    /// </summary>
    public void InsertAfter(ScannedDocument existing, ScannedDocument document)
    {
        lock (_documents)
        {
            var index = _documents.IndexOf(existing);
            _documents.Insert(index < 0 ? _documents.Count : index + 1, document);
        }
        OnChanged();
    }

    public void Remove(ScannedDocument document)
    {
        lock (_documents)
        {
            _documents.Remove(document);
        }
        document.Dispose();
        OnChanged();
    }

    /// <summary>
    /// Drops the documents that have nothing left to do, leaving the outstanding ones so a failed upload
    /// can still be retried.
    /// </summary>
    public void RemoveCompleted()
    {
        List<ScannedDocument> removed;
        lock (_documents)
        {
            removed = _documents.Where(x => x.Status == DocumentStatus.Done).ToList();
            _documents.RemoveAll(x => x.Status == DocumentStatus.Done);
        }
        foreach (var document in removed)
        {
            document.Dispose();
        }
        OnChanged();
    }

    public void Clear()
    {
        List<ScannedDocument> removed;
        lock (_documents)
        {
            removed = _documents.ToList();
            _documents.Clear();
        }
        foreach (var document in removed)
        {
            document.Dispose();
        }
        OnChanged();
    }

    public void NotifyChanged() => OnChanged();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
