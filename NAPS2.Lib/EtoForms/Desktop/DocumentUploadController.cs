using NAPS2.PostScan;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// Drives the manual upload button: sends everything waiting in the queue to its target systems and,
/// once that succeeded, clears the finished documents out of the window and the temp folder.
/// </summary>
public class DocumentUploadController
{
    private readonly DocumentUploadQueue _queue;
    private readonly DocumentUploadService _uploadService;
    private readonly ErrorOutput _errorOutput;
    private readonly ImageListActions _imageListActions;
    private readonly UiImageList _imageList;

    public DocumentUploadController(DocumentUploadQueue queue, DocumentUploadService uploadService,
        ErrorOutput errorOutput, ImageListActions imageListActions, UiImageList imageList)
    {
        _queue = queue;
        _uploadService = uploadService;
        _errorOutput = errorOutput;
        _imageListActions = imageListActions;
        _imageList = imageList;
    }

    /// <summary>
    /// Whether there is anything for the operator to upload right now.
    /// </summary>
    public bool HasPendingDocuments => _queue.HasPending;

    public async Task UploadPendingDocuments()
    {
        var pending = _queue.Documents
            .Where(x => x.Status is DocumentUploadStatus.Pending or DocumentUploadStatus.Failed)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var succeeded = 0;
        foreach (var document in pending)
        {
            if (await _uploadService.UploadAsync(document))
            {
                succeeded++;
            }
            // Surface progress as each document finishes rather than only at the end.
            _queue.NotifyChanged();
        }

        if (succeeded < pending.Count)
        {
            // Failed documents stay in the queue so the operator can fix the cause and press upload again.
            _errorOutput.DisplayError(string.Format(UiStrings.UploadSomeFailed, pending.Count - succeeded, pending.Count));
            _queue.RemoveUploaded();
            return;
        }

        CleanupCompleted(pending);
    }

    /// <summary>
    /// Removes documents that were uploaded successfully, for the profiles that ask for it.
    /// </summary>
    private void CleanupCompleted(IReadOnlyList<PendingDocument> uploaded)
    {
        var cleanupWanted = uploaded
            .Where(x => DocumentWorkflowSettings.ForProfile(x.Profile).CleanupAfterCompletion)
            .ToList();

        _queue.RemoveUploaded();

        if (cleanupWanted.Count == 0)
        {
            return;
        }

        // Mark as saved first so closing the window doesn't warn about unsaved pages, then drop them.
        var cleanedPages = cleanupWanted.SelectMany(x => x.Context.Images).ToList();
        _imageList.MarkSaved(_imageList.CurrentState, cleanedPages);
        // Only clear the window when it holds nothing beyond the archived documents. The queue can now
        // also contain documents whose automatic upload failed, and those may be retried long after the
        // operator has started scanning something else -- which must not be thrown away here.
        if (cleanupWanted.Count == uploaded.Count && !_queue.HasPending &&
            _imageList.Images.Count <= cleanedPages.Count)
        {
            Invoker.Current.Invoke(() => _imageListActions.DeleteAll());
        }
    }
}
