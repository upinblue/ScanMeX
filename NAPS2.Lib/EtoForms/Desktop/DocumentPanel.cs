using System.Collections.ObjectModel;
using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.Images;
using NAPS2.PostScan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// The document list beside the pages: every document this session produced, and an inspector for the
/// one selected.
/// </summary>
/// <remarks>
/// The scan window could only ever show pages. Whether a document had reached the archive was recorded
/// in a notification that scrolled away, a barcode read off the wrong code on the sheet could not be
/// corrected at all, and a document waiting for the upload button was invisible until the button was
/// pressed. All three are the same missing thing: somewhere for a document to be.
///
/// Split into a list and an inspector because the two answer different questions -- "is everything
/// through?" and "what is wrong with this one?" -- and one control trying to answer both is what made
/// the first version unreadable in a panel this narrow.
/// </remarks>
public class DocumentPanel : IDisposable
{
    private readonly DocumentQueue _queue;
    private readonly DocumentUploadController _uploadController;
    private readonly ColorScheme _colorScheme;
    private readonly Naps2Config _config;
    private readonly UiImageList _imageList;
    private readonly DocumentPageTracker _pageTracker;

    private readonly GridView _list = new() { ShowHeader = false, AllowMultipleSelection = false };
    private readonly ObservableCollection<DocumentRow> _rows = [];
    private readonly DocumentInspector _inspector;
    // Wrapping, unlike C.Secondary: four counts joined with separators do not fit one line at panel width.
    private readonly Label _summary = C.Label("");
    private readonly Button _removeFinished;
    private readonly LayoutVisibility _panelVis = new(true);
    private readonly LayoutVisibility _emptyVis = new(true);
    private readonly LayoutVisibility _removeFinishedVis = new(false);
    private readonly LayoutVisibility _listVis = new(false);

    /// <summary>
    /// How much of the panel the list may take before it starts scrolling. About six rows -- enough to
    /// see a normal batch at a glance without crowding out the inspector.
    /// </summary>
    private const int LIST_HEIGHT = 150;

    /// <summary>
    /// The width wrapping text is measured at, below the 260 the panel can be dragged down to. The
    /// layout reserves the height for this width and draws at the real one, so measuring at anything
    /// wider than the panel can actually be leaves the label a line short and cuts the sentence off.
    /// </summary>
    internal const int PANEL_WRAP_WIDTH = 200;

    private LayoutController? _layoutController;
    private Guid? _selectedId;
    private bool _suppressSelectionEvent;
    // Set while the panel tells the queue about an edit it made itself; see QueueChanged.
    private bool _notifyingQueue;
    // Set while the panel is driving the canvas's selection, so the change coming back is ignored.
    private bool _syncingSelection;
    private float _iconScale = 1f;

    public DocumentPanel(DocumentQueue queue, DocumentUploadController uploadController,
        ColorScheme colorScheme, Naps2Config config, UiImageList imageList,
        DocumentPageTracker pageTracker)
    {
        _queue = queue;
        _uploadController = uploadController;
        _colorScheme = colorScheme;
        _config = config;
        _imageList = imageList;
        _pageTracker = pageTracker;
        _inspector = new DocumentInspector(colorScheme, OnInspectorChanged, UploadOne, Discard);
        // Finished documents stay in the list on purpose -- they are the record that those pages went
        // through -- but a day's scanning leaves a long one, and this is how it gets shorter.
        _removeFinished = C.Button("", () => _uploadController.RemoveFinishedDocuments());

        _list.DataStore = _rows;
        _list.Columns.Add(new GridColumn
        {
            DataCell = new ImageTextCell(nameof(DocumentRow.Icon), nameof(DocumentRow.Text)),
            Expand = true
        });
        _list.SelectionChanged += ListSelectionChanged;
        // The grid does not inherit the theme, and its unpainted area comes out black on the Fluent
        // surface. Both the control and the area below the last row have to be told what colour they are.
        _list.BackgroundColor = _colorScheme.BackgroundColor;
        _summary.TextColor = _colorScheme.SecondaryTextColor;
        // Row icons are bitmaps, so they need the real scale; picked once here rather than per row,
        // because the rows are rebuilt on every change.
        EtoPlatform.Current.AttachDpiDependency(_list, scale =>
        {
            _iconScale = scale;
            UpdateRows();
        });

        // Pages being deleted, moved or edited in the window is DocumentPageTracker's business; it
        // notifies the queue when a document changed, and the list follows from there.
        _queue.Changed += QueueChanged;
        // Selecting pages in the canvas selects their document here, which is the other half of picking
        // a document and having its pages light up.
        _imageList.SelectionChanged += SelectionChanged;
    }

    public bool IsVisible => _panelVis.IsVisible;

    public LayoutElement CreateView(LayoutController layoutController)
    {
        _layoutController = layoutController;
        _panelVis.IsVisible = _config.Get(c => c.DocumentPanelVisible);
        UpdateRows();
        return L.Column(
            C.BodyStrong(UiStrings.DocumentPanelTitle),
            _summary.DynamicWrap(PANEL_WRAP_WIDTH),
            L.Column(
                C.Spacer(),
                C.Secondary(UiStrings.DocumentPanelEmpty)
            ).Visible(_emptyVis),
            // The list is capped rather than allowed to scale: it is a means of choosing, and letting it
            // grow with the number of documents pushes the inspector -- the part you actually work in --
            // off the bottom of the panel. It scrolls internally past that height.
            L.Column(_list.MaxHeight(LIST_HEIGHT)).Visible(_listVis),
            L.Row(_removeFinished, C.Filler()).Visible(_removeFinishedVis),
            C.Spacer(),
            L.Column(L.Scrollable(_inspector.Content)).Scale()
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
        if (_notifyingQueue)
        {
            // This is the panel's own notification, sent so the canvas headings and the toolbar follow
            // an edit made in the inspector. The panel has already brought its own row up to date, and
            // rebuilding the list here would put the inspector back to what the document says just as
            // the operator is clicking its upload button.
            return;
        }
        // Documents finish on scanner and upload threads, which must never touch the UI directly.
        Invoker.Current.Invoke(UpdateRows);
    }

    /// <summary>
    /// Brings the list in line with the queue, keeping the selection on the same document where it still
    /// exists so a background change doesn't move the inspector out from under the operator.
    /// </summary>
    private void UpdateRows()
    {
        var documents = _queue.Documents;
        if (_inspector.IsEditingIdentifier)
        {
            // An upload finishing in the background must not yank the caret out of the box either. The
            // counts still move; the rows catch up when the operator leaves the field.
            _inspector.Refresh();
            _summary.Text = Summarize(documents);
            return;
        }
        _suppressSelectionEvent = true;
        try
        {
            _rows.Clear();
            foreach (var document in documents)
            {
                _rows.Add(new DocumentRow(document, _colorScheme, _iconScale));
            }
            var index = _selectedId == null
                ? -1
                : documents.ToList().FindIndex(x => x.Id == _selectedId);
            if (index < 0 && documents.Count > 0)
            {
                // Nothing selected, or the selected document is gone: fall back to the first one that
                // still needs attention, which is what the operator is going to open anyway.
                index = documents.ToList().FindIndex(x => x.Status != DocumentStatus.Done);
                if (index < 0) index = 0;
            }
            _list.SelectedRow = index;
            _selectedId = index >= 0 ? documents[index].Id : null;
        }
        finally
        {
            _suppressSelectionEvent = false;
        }

        _inspector.Show(_selectedId == null ? null : documents.FirstOrDefault(x => x.Id == _selectedId));
        _emptyVis.IsVisible = documents.Count == 0;
        _listVis.IsVisible = documents.Count > 0;
        var finished = documents.Count(x => x.Status == DocumentStatus.Done);
        _removeFinished.Text = string.Format(UiStrings.RemoveFinishedDocuments, finished);
        _removeFinishedVis.IsVisible = finished > 0;
        _summary.Text = Summarize(documents);
        _layoutController?.Invalidate();
    }

    private void ListSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionEvent)
        {
            return;
        }
        var documents = _queue.Documents;
        var index = _list.SelectedRow;
        var document = index >= 0 && index < documents.Count ? documents[index] : null;
        _selectedId = document?.Id;
        _inspector.Show(document);
        if (document != null)
        {
            SelectPages(document);
        }
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
    /// The inspector edited its document: the row above it and the heading in the canvas both name the
    /// document by the file it would be filed as, so both follow the edit as it is made.
    /// </summary>
    /// <remarks>
    /// Every step here has to leave the focus where it is, because this runs on each keystroke in the
    /// identifier box. That is what <see cref="DocumentRow.Describe"/> is for -- the row is brought up to
    /// date in place and the grid repainted, rather than the row object being replaced, which raises a
    /// collection change that empties and refills the grid. Re-running the layout is the other one, and
    /// it is only needed when the inspector's own controls changed, which typing does not do.
    /// </remarks>
    private void OnInspectorChanged()
    {
        var documents = _queue.Documents;
        _summary.Text = Summarize(documents);
        var document = documents.FirstOrDefault(x => x.Id == _selectedId);
        var row = _rows.FirstOrDefault(x => x.Id == _selectedId);
        if (document != null && row != null)
        {
            row.Describe(document, _colorScheme, _iconScale);
            _list.Invalidate();
        }
        if (!_inspector.IsEditingIdentifier)
        {
            // Removing a barcode changes how many rows the inspector has, and controls only leave the
            // screen during a layout pass -- without this the row stays visible under the ones that
            // moved up. Never while the box is being typed in: a layout pass moves the caret out of it,
            // and nothing about the inspector's controls changes from a keystroke anyway.
            _layoutController?.Invalidate();
        }
        // The canvas headings and the upload button in the toolbar follow the queue, not this panel, so
        // an identification corrected here has to be announced: the heading names the document by the
        // file it would be filed as, and without this it went on showing the old name until a page
        // happened to move.
        _notifyingQueue = true;
        try
        {
            _queue.NotifyChanged();
        }
        finally
        {
            _notifyingQueue = false;
        }
    }

    private void UploadOne(ScannedDocument document) => _ = _uploadController.UploadDocument(document);

    private void Discard(ScannedDocument document)
    {
        ScanConsole.Document($"{document.Describe()}: discarded from the document list by the operator.");
        _queue.Remove(document);
    }

    /// <summary>
    /// Selects the document's pages in the thumbnail list, so picking a document shows what is in it.
    /// </summary>
    private void SelectPages(ScannedDocument document)
    {
        var present = _imageList.Images.ToHashSet();
        var matching = (document.WindowPages ?? []).Where(present.Contains).ToList();
        if (matching.Count == 0)
        {
            return;
        }
        // The pages coming back as a selection change must not point the panel at a document again --
        // it is already there, and the round trip would fight the operator over the inspector.
        _syncingSelection = true;
        try
        {
            _imageList.UpdateSelection(ListSelection.From(matching));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// The other direction: pages picked in the canvas point the panel at the document they are in, so
    /// clicking a page shows what will happen to it.
    /// </summary>
    /// <remarks>
    /// Only when the selection is inside a single document. A selection spanning two of them has no one
    /// answer, and picking either would be a guess the operator then has to notice and undo.
    /// </remarks>
    private void SelectionChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }
        Invoker.Current.Invoke(() =>
        {
            var documents = _imageList.Selection
                .Select(_pageTracker.DocumentFor)
                .Distinct()
                .ToList();
            if (documents.Count != 1 || documents[0] == null || documents[0]!.Id == _selectedId)
            {
                return;
            }
            var document = documents[0]!;
            _selectedId = document.Id;
            var index = _queue.Documents.ToList().FindIndex(x => x.Id == document.Id);
            _suppressSelectionEvent = true;
            try
            {
                _list.SelectedRow = index;
            }
            finally
            {
                _suppressSelectionEvent = false;
            }
            _inspector.Show(document);
            _layoutController?.Invalidate();
        });
    }

    public void Dispose()
    {
        _queue.Changed -= QueueChanged;
        _imageList.SelectionChanged -= SelectionChanged;
    }

    /// <summary>
    /// One row of the list: a status icon and a single line of text. Deliberately thin -- the detail
    /// belongs to the inspector, and a list that repeats it cannot stay readable at panel width.
    /// </summary>
    /// <remarks>
    /// The two values are settable, and <see cref="Describe"/> is how a row already in the list is
    /// brought up to date. The grid reads them through property bindings every time it paints a cell, so
    /// changing them and invalidating the control shows the new text -- whereas replacing the row object
    /// raises a collection change, and Eto answers that by rebuilding the grid: the selection is cleared,
    /// the selection event comes back round to the panel saying nothing is selected, and the inspector
    /// the operator is working in empties itself. That is why picking a barcode used to require selecting
    /// the document again afterwards.
    /// </remarks>
    private class DocumentRow
    {
        public DocumentRow(ScannedDocument document, ColorScheme colorScheme, float iconScale)
        {
            Describe(document, colorScheme, iconScale);
        }

        public Image? Icon { get; private set; }

        public string Text { get; private set; } = "";

        /// <summary>
        /// Which document the row stands for, so it can be found again without going by position: the
        /// list is only rebuilt when the operator is not typing, so while they are, the row at a
        /// document's index in the queue may still be a different document's.
        /// </summary>
        public Guid Id { get; private set; }

        public void Describe(ScannedDocument document, ColorScheme colorScheme, float iconScale)
        {
            Id = document.Id;
            var severity = DocumentInspector.SeverityOf(document.Status);
            var color = severity == Notifications.NotificationSeverity.Neutral
                ? colorScheme.SecondaryTextColor
                : colorScheme.GetSeverityColor(severity);
            Icon = EtoPlatform.Current.IconProvider
                .GetIcon(DocumentInspector.IconOf(document.Status), iconScale)?.Tint(color);
            Text = $"{DocumentInspector.ResolveName(document) ?? UiStrings.DocumentNameMissingShort}  ·  " +
                   string.Format(UiStrings.DocumentPageCount, document.PageCount);
        }
    }
}
