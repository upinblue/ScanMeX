using NAPS2.EtoForms.Layout;
using NAPS2.Images;
using NAPS2.PostScan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// The document list beside the pages: every document this session produced, what it is called, what it
/// is filed under, which barcodes it carries and whether it got where it was going.
/// </summary>
/// <remarks>
/// This exists because the scan window could only ever show pages. Whether a document had reached the
/// archive was recorded in a notification that scrolled away, a barcode read off the wrong code on the
/// sheet could not be corrected at all, and a document waiting for the upload button was invisible until
/// the button was pressed. All three are the same missing thing: somewhere for a document to be.
/// </remarks>
public class DocumentPanel : IDisposable
{
    private readonly DocumentQueue _queue;
    private readonly DocumentUploadController _uploadController;
    private readonly ColorScheme _colorScheme;
    private readonly Naps2Config _config;
    private readonly UiImageList _imageList;

    private readonly Dictionary<Guid, DocumentCardView> _cards = new();
    private readonly LayoutColumn _list = L.Column().Spacing(8);
    private readonly LayoutVisibility _emptyVis = new(true);
    private readonly LayoutVisibility _panelVis = new(true);
    // The layout engine re-shows controls as it lays them out, so Control.Visible doesn't stick; hiding
    // has to go through the layout's own visibility.
    private readonly LayoutVisibility _uploadAllVis = new(false);
    private readonly Eto.Forms.Label _summary = C.Secondary("");
    private readonly Eto.Forms.Button _uploadAll;

    private LayoutController? _layoutController;

    public DocumentPanel(DocumentQueue queue, DocumentUploadController uploadController,
        ColorScheme colorScheme, Naps2Config config, UiImageList imageList)
    {
        _queue = queue;
        _uploadController = uploadController;
        _colorScheme = colorScheme;
        _config = config;
        _imageList = imageList;
        _uploadAll = C.Button(UiStrings.UploadPendingDocuments,
            () => _ = _uploadController.UploadPendingDocuments());
        _queue.Changed += QueueChanged;
    }

    public bool IsVisible => _panelVis.IsVisible;

    public LayoutElement CreateView(LayoutController layoutController)
    {
        _layoutController = layoutController;
        _panelVis.IsVisible = _config.Get(c => c.DocumentPanelVisible);
        UpdateCards();
        return L.Column(
            C.BodyStrong(UiStrings.DocumentPanelTitle),
            _summary,
            L.Column(
                C.Spacer(),
                C.Secondary(UiStrings.DocumentPanelEmpty)
            ).Visible(_emptyVis),
            L.Column(L.Scrollable(_list)).Scale(),
            L.Row(_uploadAll, C.Filler()).Visible(_uploadAllVis)
        ).Padding(left: 10, right: 10, top: 8, bottom: 8).Spacing(6).Visible(_panelVis);
    }

    public void ToggleVisibility()
    {
        _panelVis.IsVisible = !_panelVis.IsVisible;
        _config.User.Set(c => c.DocumentPanelVisible, _panelVis.IsVisible);
        _layoutController?.Invalidate();
    }

    private void QueueChanged(object? sender, EventArgs e)
    {
        // Documents finish on scanner and upload threads, which must never touch the UI directly.
        Invoker.Current.Invoke(UpdateCards);
    }

    /// <summary>
    /// Brings the list in line with the queue. Cards for documents that are still there are refreshed
    /// rather than replaced, so an operator typing an identification keeps the caret.
    /// </summary>
    private void UpdateCards()
    {
        var documents = _queue.Documents;
        var live = documents.Select(x => x.Id).ToHashSet();

        foreach (var gone in _cards.Keys.Except(live).ToList())
        {
            _list.Children.Remove(_cards[gone].Content);
            _cards.Remove(gone);
        }
        foreach (var document in documents)
        {
            if (_cards.TryGetValue(document.Id, out var existing))
            {
                existing.Refresh();
                continue;
            }
            var card = new DocumentCardView(document, _colorScheme, OnCardChanged, UploadOne, Discard,
                SelectPages);
            _cards.Add(document.Id, card);
            _list.Children.Add(card.Content);
        }

        _emptyVis.IsVisible = documents.Count == 0;
        _summary.Text = Summarize(documents);
        _uploadAll.Enabled = _uploadController.HasPendingDocuments;
        _uploadAllVis.IsVisible = documents.Count > 0;
        _layoutController?.Invalidate();
    }

    private string Summarize(IReadOnlyList<ScannedDocument> documents)
    {
        if (documents.Count == 0)
        {
            return "";
        }
        var done = documents.Count(x => x.Status == DocumentStatus.Done);
        var failed = documents.Count(x => x.Status == DocumentStatus.Failed);
        var waiting = documents.Count(x => x.Status == DocumentStatus.NeedsIdentifier);
        var parts = new List<string> { string.Format(UiStrings.DocumentSummaryTotal, documents.Count) };
        if (done > 0) parts.Add(string.Format(UiStrings.DocumentSummaryDone, done));
        if (failed > 0) parts.Add(string.Format(UiStrings.DocumentSummaryFailed, failed));
        if (waiting > 0) parts.Add(string.Format(UiStrings.DocumentSummaryNeedsIdentifier, waiting));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A card edited its document. The queue isn't told, because it would rebuild every other card and
    /// take the caret out of the box being typed into; only the parts that depend on all documents at
    /// once are recomputed.
    /// </summary>
    private void OnCardChanged()
    {
        _summary.Text = Summarize(_queue.Documents);
        _uploadAll.Enabled = _uploadController.HasPendingDocuments;
    }

    private void UploadOne(ScannedDocument document) => _ = _uploadController.UploadDocument(document);

    private void Discard(ScannedDocument document)
    {
        ScanConsole.Document($"{document.Describe()}: discarded from the document list by the operator.");
        _queue.Remove(document);
    }

    /// <summary>
    /// Selects the document's pages in the thumbnail list, so clicking a document shows what is in it.
    /// </summary>
    private void SelectPages(ScannedDocument document)
    {
        var pages = document.Pages.ToHashSet();
        var matching = _imageList.Images
            .Where(x => pages.Contains(x.GetImageWeakReference().ProcessedImage))
            .ToList();
        if (matching.Count > 0)
        {
            _imageList.UpdateSelection(ListSelection.From(matching));
        }
    }

    public void Dispose()
    {
        _queue.Changed -= QueueChanged;
    }
}
