using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;
using NAPS2.ImportExport;
using NAPS2.PostScan;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// One document in the document list: what it will be called, what it is filed under, which barcodes
/// were found on it, and how far it has got.
/// </summary>
/// <remarks>
/// The controls are created once and refreshed in place rather than rebuilt. An operator correcting a
/// misread barcode is typing into these boxes while the queue is still changing around them -- another
/// document finishing its upload raises Changed -- and rebuilding would take the caret away mid-word.
/// </remarks>
public class DocumentCardView
{
    private readonly ScannedDocument _document;
    private readonly ColorScheme _colorScheme;
    private readonly Action _onChanged;
    private readonly Action<ScannedDocument> _onUpload;
    private readonly Action<ScannedDocument> _onDiscard;
    private readonly Action<ScannedDocument> _onSelectPages;

    private readonly Drawable _card = new();
    private readonly ImageView _statusIcon = new();
    private readonly Label _name = C.NoWrap("");
    private readonly Label _pages = C.Secondary("");
    // Wrapping, unlike C.Secondary: the panel is narrow and these sentences explain why a document is
    // sitting there, which is exactly the text that must not be cut off mid-word.
    private readonly Label _statusText = C.Label("");
    private readonly Label _message = C.Label("");
    private readonly Label _identifierLabel = C.Secondary("");
    private readonly TextBox _identifier = new();
    private readonly LayoutColumn _barcodes = L.Column().Spacing(2);
    private readonly Button _uploadButton;
    private readonly Button _discardButton;
    // The layout engine re-shows controls as it lays them out, so Control.Visible doesn't stick.
    private readonly LayoutVisibility _messageVis = new(false);
    private readonly LayoutVisibility _uploadVis = new(true);
    // Labels paint their own background, which on a tinted card shows as a pale box around every line.
    // They are repainted with the card's tint whenever the status changes it.
    private readonly List<Control> _tinted = [];
    // Rebuilt with the barcode rows, so it is kept apart from the card's permanent controls rather than
    // growing by two entries every time a barcode is removed.
    private readonly List<Control> _tintedRows = [];

    private bool _suppressEvents;

    public DocumentCardView(ScannedDocument document, ColorScheme colorScheme, Action onChanged,
        Action<ScannedDocument> onUpload, Action<ScannedDocument> onDiscard,
        Action<ScannedDocument> onSelectPages)
    {
        _document = document;
        _colorScheme = colorScheme;
        _onChanged = onChanged;
        _onUpload = onUpload;
        _onDiscard = onDiscard;
        _onSelectPages = onSelectPages;

        _name.Font = new Font(_name.Font.Family, _name.Font.Size, FontStyle.Bold);
        _identifier.TextChanged += IdentifierChanged;
        _uploadButton = C.Button(UiStrings.UploadDocumentAction, () => _onUpload(_document));
        _discardButton = C.Button(UiStrings.DiscardDocumentAction, () => _onDiscard(_document));

        _card.Paint += PaintCard;
        _card.MouseUp += (_, _) => _onSelectPages(_document);
        _tinted.AddRange([_statusIcon, _name, _pages, _statusText, _message, _identifierLabel]);

        Content = L.Overlay(
            _card,
            L.Column(
                L.Row(_statusIcon.AlignCenter(), _name.Scale(), _pages.AlignCenter()).Spacing(6),
                _statusText,
                L.Column(_message).Visible(_messageVis),
                C.Spacer(),
                _identifierLabel,
                _identifier,
                _barcodes,
                C.Spacer(),
                L.Row(
                    L.Column(_uploadButton).Visible(_uploadVis),
                    _discardButton,
                    C.Filler()
                ).Spacing(4)
            ).Padding(10).Spacing(3)
        );

        BuildBarcodeRows();
        Refresh();
    }

    public LayoutElement Content { get; }

    public ScannedDocument Document => _document;

    /// <summary>
    /// The outcome the card's tint reports. An operator scanning a stack reads the colour before the
    /// text, so a document still waiting for a number must not look like one that reached the archive.
    /// </summary>
    private NotificationSeverity Severity => _document.Status switch
    {
        DocumentStatus.Done => NotificationSeverity.Success,
        DocumentStatus.Failed => NotificationSeverity.Error,
        DocumentStatus.NeedsIdentifier => NotificationSeverity.Warning,
        _ => NotificationSeverity.Neutral
    };

    private string? SeverityIconName => Severity switch
    {
        NotificationSeverity.Success => "status_success_small",
        NotificationSeverity.Warning => "status_warning_small",
        NotificationSeverity.Error => "status_error_small",
        _ => "document_small"
    };

    /// <summary>
    /// Brings the card up to date with the document. Called whenever the queue reports a change, so it
    /// must not touch anything the operator could be editing -- the identifier box is only written here
    /// when the value came from somewhere other than this box.
    /// </summary>
    public void Refresh()
    {
        _suppressEvents = true;
        try
        {
            _name.Text = ResolveName();
            // The name is deliberately not wrapped -- a wrapping title makes the card jump around as the
            // identification is typed -- so the full value has to be reachable from the tooltip.
            _name.ToolTip = _document.SavedPath ?? _name.Text;
            _pages.Text = string.Format(UiStrings.DocumentPageCount, _document.PageCount);
            _statusText.Text = DescribeStatus();
            _statusText.TextColor = _colorScheme.SecondaryTextColor;
            _message.Text = _document.Message ?? "";
            _messageVis.IsVisible = !string.IsNullOrEmpty(_document.Message);
            _message.TextColor = _colorScheme.GetSeverityColor(Severity);

            var iconName = SeverityIconName;
            var iconColor = Severity == NotificationSeverity.Neutral
                ? _colorScheme.SecondaryTextColor
                : _colorScheme.GetSeverityColor(Severity);
            EtoPlatform.Current.AttachDpiDependency(_statusIcon,
                scale => _statusIcon.Image =
                    EtoPlatform.Current.IconProvider.GetIcon(iconName!, scale)?.Tint(iconColor));

            _identifierLabel.Text = _document.Workflow.IdPromptLabel is { Length: > 0 } label
                ? label
                : UiStrings.DocumentIdentifierLabel;
            if (!_identifier.HasFocus)
            {
                _identifier.Text = _document.Identifier ?? "";
            }
            _identifier.PlaceholderText = _document.Status == DocumentStatus.NeedsIdentifier
                ? UiStrings.DocumentIdentifierRequired
                : "";

            var busy = _document.Status == DocumentStatus.Working;
            _uploadButton.Enabled = !busy && _document.HasEverythingItNeeds() &&
                                    _document.Status != DocumentStatus.Done;
            _uploadButton.Text = _document.Status == DocumentStatus.Failed
                ? UiStrings.RetryDocumentAction
                : UiStrings.UploadDocumentAction;
            _uploadVis.IsVisible = DocumentUploadService.HasAnyTarget(_document.Profile) ||
                                   _document.Workflow.SaveLocally;
            _discardButton.Enabled = !busy;
            var tint = _colorScheme.GetSeverityBackgroundColor(Severity);
            foreach (var control in _tinted.Concat(_tintedRows))
            {
                control.BackgroundColor = tint;
            }
            _card.Invalidate();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// What the document will be called. Before it has been written this is the template expanded against
    /// the document as it stands, which is what makes the effect of correcting the identifier visible
    /// before committing to it.
    /// </summary>
    private string ResolveName()
    {
        if (_document.SavedPath != null)
        {
            return Path.GetFileName(_document.SavedPath);
        }
        var template = _document.Workflow.GetDocumentNameTemplate();
        try
        {
            var resolved = new FileNamePlaceholders()
                .SubstitutePlaceholders(template, _document.BuildContext(template));
            // With no identification yet, "$(id).pdf" expands to ".pdf" -- a name that looks like a bug
            // rather than like the blank it is. Say what is actually missing.
            if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(resolved)))
            {
                return UiStrings.DocumentNameNotYetKnown;
            }
            return resolved;
        }
        catch (Exception)
        {
            // A template the placeholder engine chokes on is the profile's problem, not this label's.
            return template;
        }
    }

    private string DescribeStatus() => _document.Status switch
    {
        DocumentStatus.NeedsIdentifier => UiStrings.DocumentStatusNeedsIdentifier,
        DocumentStatus.Working => UiStrings.DocumentStatusWorking,
        DocumentStatus.Failed => UiStrings.DocumentStatusFailed,
        DocumentStatus.Done => _document.CompletedTargets.Count > 0
            ? string.Format(UiStrings.DocumentStatusUploaded, string.Join(", ", _document.CompletedTargets))
            : _document.IsSavedLocally
                ? UiStrings.DocumentStatusSaved
                : UiStrings.DocumentStatusDone,
        _ => _document.IsSavedLocally
            ? UiStrings.DocumentStatusSavedWaiting
            : UiStrings.DocumentStatusWaiting
    };

    private void IdentifierChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }
        _document.SetIdentifier(_identifier.Text, DocumentBarcodeSource.Manual);
        // A document held back for want of a number is released as soon as one is typed, and taken back
        // if the box is emptied again -- otherwise the upload button's state lags a keystroke behind.
        if (_document.Status is DocumentStatus.NeedsIdentifier or DocumentStatus.Pending)
        {
            _document.Status = _document.HasEverythingItNeeds()
                ? DocumentStatus.Pending
                : DocumentStatus.NeedsIdentifier;
        }
        Refresh();
        _onChanged();
    }

    /// <summary>
    /// One row per barcode found on the document, each editable and removable. Rebuilt as a block
    /// whenever the set of barcodes changes, which only happens on an explicit add or remove.
    /// </summary>
    private void BuildBarcodeRows()
    {
        _barcodes.Children.Clear();
        _tintedRows.Clear();
        if (_document.Barcodes.Count == 0)
        {
            var none = C.Secondary(UiStrings.DocumentNoBarcodes);
            _tintedRows.Add(none);
            _barcodes.Children.Add(none);
        }
        foreach (var barcode in _document.Barcodes.ToList())
        {
            _barcodes.Children.Add(CreateBarcodeRow(barcode));
        }
        var add = C.Link(UiStrings.AddBarcodeAction, AddBarcode);
        _tintedRows.Add(add);
        _barcodes.Children.Add(add);
    }

    private LayoutElement CreateBarcodeRow(DocumentBarcode barcode)
    {
        var value = new TextBox { Text = barcode.Value, ToolTip = barcode.Describe() };
        var current = barcode;
        value.TextChanged += (_, _) =>
        {
            _document.ReplaceBarcode(current, value.Text ?? "");
            current = _document.Barcodes.FirstOrDefault(x => x.Value == (value.Text ?? "")) ?? current;
            Refresh();
            _onChanged();
        };
        // The symbology is what tells a real barcode from one read out of the print noise, so it is on
        // the row rather than only in the tooltip.
        var format = C.Secondary(barcode.Source == DocumentBarcodeSource.Manual
            ? UiStrings.BarcodeSourceManual
            : barcode.Format ?? "?");
        _tintedRows.Add(format);
        var useAs = C.Link(UiStrings.UseAsIdentifierAction, () =>
        {
            _identifier.Text = current.Value;
            _document.SetIdentifier(current.Value, DocumentBarcodeSource.Manual);
            Refresh();
            _onChanged();
        });
        var remove = C.Link(UiStrings.RemoveBarcodeAction, () =>
        {
            _document.RemoveBarcode(current);
            BuildBarcodeRows();
            Refresh();
            _onChanged();
        });
        _tintedRows.Add(useAs);
        _tintedRows.Add(remove);
        // The value gets a line of its own. Sharing one with the two actions squeezed a twelve-digit
        // code down to eight visible characters, which defeats the point of showing it for checking.
        return L.Column(
            L.Row(value.Scale(), format.AlignCenter()).Spacing(6),
            L.Row(C.Filler(), useAs, remove).Spacing(10)
        ).Spacing(1).SpacingAfter(6);
    }

    private void AddBarcode()
    {
        _document.AddBarcode(new DocumentBarcode("", null, -1, DocumentBarcodeSource.Manual));
        BuildBarcodeRows();
        _onChanged();
    }

    private void PaintCard(object? sender, PaintEventArgs e)
    {
        var bounds = new RectangleF(PointF.Empty, e.ClipRectangle.Size);
        e.Graphics.FillRectangle(_colorScheme.GetSeverityBackgroundColor(Severity), bounds);
        e.Graphics.DrawRectangle(_colorScheme.GetSeverityBorderColor(Severity),
            new RectangleF(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));
    }
}
