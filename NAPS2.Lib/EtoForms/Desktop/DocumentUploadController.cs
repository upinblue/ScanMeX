using NAPS2.PostScan;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// Drives the upload button and the per-document actions in the document list: takes documents the rest
/// of the way to their target systems and, once that succeeded, clears the finished ones out of the
/// window.
/// </summary>
/// <remarks>
/// The work itself goes through <see cref="DocumentPipeline.Advance"/>, the same method the automatic
/// trigger uses, so a document uploaded by hand is written, named and archived exactly like one uploaded
/// automatically. The two used to be separate code paths and drifted.
/// </remarks>
public class DocumentUploadController
{
    private readonly DocumentQueue _queue;
    private readonly DocumentPipeline _pipeline;
    private readonly ErrorOutput _errorOutput;

    public DocumentUploadController(DocumentQueue queue, DocumentPipeline pipeline, ErrorOutput errorOutput)
    {
        _queue = queue;
        _pipeline = pipeline;
        _errorOutput = errorOutput;
    }

    /// <summary>
    /// Whether there is anything for the operator to upload right now. Documents still missing an
    /// identifier don't count: they are outstanding, but pressing upload must not file them.
    /// </summary>
    public bool HasPendingDocuments => _queue.HasReadyToUpload;

    /// <summary>
    /// Whether anything at all is unfinished, including documents held back for want of an identifier.
    /// </summary>
    public bool HasOutstandingDocuments => _queue.HasOutstanding;

    /// <summary>
    /// Clears the finished documents out of the list, pages and all. Nothing is deleted from disk: they
    /// have been written and, where the profile asked for it, archived.
    /// </summary>
    public void RemoveFinishedDocuments() => _pipeline.RemoveFinished();

    public async Task UploadPendingDocuments()
    {
        var ready = _queue.ReadyToUpload;
        var blocked = _queue.Outstanding.Count - ready.Count;
        if (ready.Count == 0)
        {
            if (blocked > 0)
            {
                // Pressing upload and having nothing happen is the failure this whole console exists for.
                ScanConsole.Upload(
                    $"Upload pressed, but all {blocked} outstanding document(s) are still missing an " +
                    "identifier. Enter one in the document list first.");
                _errorOutput.DisplayError(string.Format(UiStrings.UploadBlockedByMissingId, blocked));
            }
            return;
        }

        await UploadDocuments(ready);

        if (blocked > 0)
        {
            ScanConsole.Upload(
                $"{blocked} document(s) were left alone because they are still missing an identifier.");
        }
    }

    /// <summary>
    /// Uploads one document, for the per-row button in the document list.
    /// </summary>
    public async Task UploadDocument(ScannedDocument document)
    {
        if (!document.HasEverythingItNeeds())
        {
            _errorOutput.DisplayError(string.Format(UiStrings.UploadBlockedByMissingId, 1));
            return;
        }
        await UploadDocuments([document]);
    }

    private async Task UploadDocuments(IReadOnlyList<ScannedDocument> documents)
    {
        var succeeded = 0;
        foreach (var document in documents)
        {
            await _pipeline.Advance(document, triggeredByOperator: true);
            if (document.Status == DocumentStatus.Done)
            {
                succeeded++;
            }
            // Surface progress as each document finishes rather than only at the end.
            _queue.NotifyChanged();
        }

        if (succeeded < documents.Count)
        {
            // Failed documents stay in the queue so the operator can fix the cause and press upload again.
            _errorOutput.DisplayError(
                string.Format(UiStrings.UploadSomeFailed, documents.Count - succeeded, documents.Count));
        }
    }
}
