using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Notifications;
using NAPS2.ImportExport;
using NAPS2.PostScan;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// The detail half of the document panel: everything about the one document currently selected.
/// </summary>
/// <remarks>
/// One inspector rather than one card per document. Each document used to carry its own copy of every
/// control, which made the panel a wall of repeated boxes where it was never obvious which barcode
/// belonged to which document -- and, because the stack grew and shrank, the cards ended up drawn on
/// top of each other. Here the controls exist once and are pointed at whichever document is selected.
/// </remarks>
public class DocumentInspector
{
    private readonly ColorScheme _colorScheme;
    private readonly Action _onChanged;
    private readonly Action<ScannedDocument> _onUpload;
    private readonly Action<ScannedDocument> _onDiscard;

    private readonly Label _title = C.BodyStrong("");
    private readonly ImageView _statusIcon = new();
    private readonly Label _status = C.Label("");
    private readonly Label _identifierLabel = C.Secondary("");
    private readonly TextBox _identifier = new();
    private readonly LayoutColumn _barcodes = L.Column().Spacing(1);
    private readonly Label _fileName = C.NoWrap("");
    private readonly Label _targets = C.Secondary("");
    private readonly Button _uploadButton;
    private readonly Button _discardButton;

    private readonly LayoutVisibility _detailVis = new(false);
    private readonly LayoutVisibility _emptyVis = new(true);

    private ScannedDocument? _document;
    private bool _suppressEvents;
    private float _iconScale = 1f;
    // Rebuilt with the barcode rows; the radio group has to be recreated when the set of values changes,
    // because Eto ties radio buttons together through the instance the first one was constructed with.
    private readonly List<RadioButton> _barcodeChoices = [];
    private RadioButton? _ownValueChoice;

    public DocumentInspector(ColorScheme colorScheme, Action onChanged, Action<ScannedDocument> onUpload,
        Action<ScannedDocument> onDiscard)
    {
        _colorScheme = colorScheme;
        _onChanged = onChanged;
        _onUpload = onUpload;
        _onDiscard = onDiscard;

        // Subscribed once, here. This used to be done from Refresh, which runs on every keystroke in the
        // identifier box and on every queue change, and each call adds another DpiChangedAfterParent
        // handler that is never removed -- so a few minutes of correcting barcodes left hundreds of
        // subscriptions on one ImageView, all of them re-tinting the icon on the next DPI change.
        EtoPlatform.Current.AttachDpiDependency(_statusIcon, scale =>
        {
            _iconScale = scale;
            UpdateStatusIcon();
        });

        _identifier.TextChanged += IdentifierChanged;
        // The list row and the radio buttons are brought up to date once the box is done being typed in;
        // see IsEditingIdentifier for why they cannot be touched while it has focus.
        _identifier.LostFocus += (_, _) =>
        {
            Refresh();
            _onChanged();
        };
        _uploadButton = C.Button(UiStrings.UploadDocumentAction, () =>
        {
            if (_document != null) _onUpload(_document);
        });
        _discardButton = C.Button(UiStrings.DiscardDocumentAction, () =>
        {
            if (_document != null) _onDiscard(_document);
        });

        Content = L.Column(
            C.BodyStrong(UiStrings.DocumentInspectorTitle),
            L.Column(C.Label(UiStrings.DocumentInspectorNoSelection)
                .DynamicWrap(DocumentPanel.PANEL_WRAP_WIDTH)).Visible(_emptyVis),
            L.Column(
                L.Row(_statusIcon.AlignCenter(), _title.Scale()).Spacing(6),
                _status.DynamicWrap(DocumentPanel.PANEL_WRAP_WIDTH),
                C.Spacer(),
                _identifierLabel,
                _identifier,
                C.Spacer(),
                C.Secondary(UiStrings.DocumentBarcodesSection),
                _barcodes,
                C.Spacer(),
                L.Row(C.Secondary(UiStrings.DocumentFileNameLabel).Width(70), _fileName.Scale()),
                L.Row(C.Secondary(UiStrings.DocumentTargetsLabel).Width(70), _targets.Scale()),
                C.Spacer(),
                L.Row(_uploadButton, _discardButton, C.Filler()).Spacing(4)
            ).Visible(_detailVis)
        ).Spacing(3);
    }

    public LayoutElement Content { get; }

    public ScannedDocument? Document => _document;

    /// <summary>
    /// Whether the operator is currently typing an identification.
    /// </summary>
    /// <remarks>
    /// Everything that reacts to the value changing has to leave the focus alone while this is true.
    /// Setting a radio button's Checked, replacing the row in the document list, or re-running the
    /// layout all move the caret out of the box, and since the value changes on every keystroke the
    /// effect was that only one character could be typed before the box lost focus and the selection
    /// jumped away. The deferred work happens on LostFocus instead.
    /// </remarks>
    public bool IsEditingIdentifier => _identifier.HasFocus;

    /// <summary>
    /// Points the inspector at a document, or at nothing.
    /// </summary>
    public void Show(ScannedDocument? document)
    {
        var changedDocument = !ReferenceEquals(_document, document);
        _document = document;
        _emptyVis.IsVisible = document == null;
        _detailVis.IsVisible = document != null;
        if (document != null && changedDocument)
        {
            BuildBarcodeRows();
        }
        Refresh();
    }

    /// <summary>
    /// Brings the inspector up to date with the document it is pointed at. Called whenever the queue
    /// reports a change, so it must not overwrite the identifier box while it is being typed into.
    /// </summary>
    public void Refresh()
    {
        if (_document == null)
        {
            return;
        }
        _suppressEvents = true;
        try
        {
            var severity = SeverityOf(_document.Status);
            _title.Text = string.Format(UiStrings.DocumentListRow, _document.SequenceIndex + 1,
                _document.PageCount);
            _status.Text = DescribeStatus(_document);
            _status.TextColor = severity == NotificationSeverity.Neutral
                ? _colorScheme.SecondaryTextColor
                : _colorScheme.GetSeverityColor(severity);
            UpdateStatusIcon();

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

            _fileName.Text = ResolveName(_document) ?? UiStrings.DocumentNameMissingShort;
            _fileName.ToolTip = _document.SavedPath ?? _fileName.Text;
            _targets.Text = DescribeTargets(_document);

            var busy = _document.Status == DocumentStatus.Working;
            _uploadButton.Enabled = !busy && _document.HasEverythingItNeeds() &&
                                    _document.Status != DocumentStatus.Done;
            _uploadButton.Text = _document.Status == DocumentStatus.Failed
                ? UiStrings.RetryDocumentAction
                : UiStrings.UploadDocumentAction;
            _discardButton.Enabled = !busy;
            if (!IsEditingIdentifier)
            {
                SyncBarcodeSelection();
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// Repaints the status badge from the document's current status, at the scale the screen is at.
    /// </summary>
    private void UpdateStatusIcon()
    {
        if (_document == null)
        {
            return;
        }
        var severity = SeverityOf(_document.Status);
        var iconColor = severity == NotificationSeverity.Neutral
            ? _colorScheme.SecondaryTextColor
            : _colorScheme.GetSeverityColor(severity);
        _statusIcon.Image = EtoPlatform.Current.IconProvider
            .GetIcon(IconOf(_document.Status), _iconScale)?.Tint(iconColor);
    }

    public static NotificationSeverity SeverityOf(DocumentStatus status) => status switch
    {
        DocumentStatus.Done => NotificationSeverity.Success,
        DocumentStatus.Failed => NotificationSeverity.Error,
        DocumentStatus.NeedsIdentifier => NotificationSeverity.Warning,
        _ => NotificationSeverity.Neutral
    };

    public static string IconOf(DocumentStatus status) => status switch
    {
        DocumentStatus.Done => "status_success_small",
        DocumentStatus.Failed => "status_error_small",
        DocumentStatus.NeedsIdentifier => "status_warning_small",
        _ => "document_small"
    };

    public static string DescribeStatus(ScannedDocument document) => document.Status switch
    {
        DocumentStatus.NeedsIdentifier => UiStrings.DocumentStatusNeedsIdentifier,
        DocumentStatus.Working => UiStrings.DocumentStatusWorking,
        DocumentStatus.Failed => document.Message ?? UiStrings.DocumentStatusFailed,
        DocumentStatus.Done => document.CompletedTargets.Count > 0
            ? string.Format(UiStrings.DocumentStatusUploaded, string.Join(", ", document.CompletedTargets))
            : document.IsSavedLocally
                ? UiStrings.DocumentStatusSaved
                : UiStrings.DocumentStatusDone,
        // Pending, which is where a document filed by a profile with nowhere else to send it stays. It is
        // not waiting for an upload -- there is no upload coming -- and saying so under every document of
        // a save-only profile describes a queue that does not exist.
        _ when !DocumentUploadService.HasAnyTarget(document.Profile) => document.IsSavedLocally
            ? UiStrings.DocumentStatusSaved
            : UiStrings.DocumentStatusDone,
        _ => document.IsSavedLocally
            ? UiStrings.DocumentStatusSavedWaiting
            : UiStrings.DocumentStatusWaiting
    };

    /// <summary>
    /// What the document will be called, or null when the identification it is named after is still
    /// missing. Before the document has been written this is the template expanded against its current
    /// state, which is what makes the effect of correcting the identification visible before committing
    /// to it.
    /// </summary>
    /// <remarks>
    /// Returns null rather than a placeholder because "$(id).pdf" with no identification expands to
    /// ".pdf", which reads as a bug rather than as the blank it is.
    /// </remarks>
    public static string? ResolveName(ScannedDocument document)
    {
        if (document.SavedPath != null)
        {
            return Path.GetFileName(document.SavedPath);
        }
        var template = document.Workflow.GetDocumentNameTemplate();
        try
        {
            var resolved = new FileNamePlaceholders()
                .SubstitutePlaceholders(template, document.BuildContext(template));
            // With no identification yet, "$(id).pdf" expands to ".pdf" -- a name that looks like a bug
            // rather than like the blank it is.
            return string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(resolved)) ? null : resolved;
        }
        catch (Exception)
        {
            // A template the placeholder engine chokes on is the profile's problem, not this label's.
            return template;
        }
    }

    private static string DescribeTargets(ScannedDocument document)
    {
        var targets = new List<string>();
        if (document.Workflow.SaveLocally) targets.Add(UiStrings.DocumentTargetLocal);
        if (document.Profile.UploadsToSharePoint()) targets.Add("SharePoint");
        if (document.Profile.UploadsToSap()) targets.Add(UiStrings.SapArchiveLink);
        return targets.Count == 0 ? UiStrings.DocumentTargetsNone : string.Join(", ", targets);
    }

    private void IdentifierChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || _document == null)
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
    /// One radio button per barcode found on the document, plus "own value".
    /// </summary>
    /// <remarks>
    /// Radio buttons rather than a "use as identification" button per row: the button said what would
    /// happen if you pressed it but never which barcode was actually in use, and with several codes on a
    /// sheet that is the only thing worth knowing. Here the selected one *is* the identification, and
    /// "own value" makes it explicit that typing something else is allowed.
    /// </remarks>
    private void BuildBarcodeRows()
    {
        _barcodes.Children.Clear();
        _barcodeChoices.Clear();
        _ownValueChoice = null;
        if (_document == null)
        {
            return;
        }

        RadioButton? first = null;
        foreach (var barcode in _document.Barcodes.ToList())
        {
            var current = barcode;
            var choice = first == null
                ? new RadioButton { Text = barcode.Value }
                : new RadioButton(first) { Text = barcode.Value };
            first ??= choice;
            choice.CheckedChanged += (_, _) =>
            {
                if (_suppressEvents || !choice.Checked || _document == null) return;
                _document.SetIdentifier(current.Value, DocumentBarcodeSource.Detected);
                Refresh();
                _onChanged();
            };
            _barcodeChoices.Add(choice);

            // Where it was found, not what kind it is. The symbology mattered while phantom reads were
            // possible; now that detection is restricted to the profile's own types, a code that turns up
            // here is one of them, and the page is what the operator needs to go and look at.
            var detail = C.Secondary(barcode.Source == DocumentBarcodeSource.Manual
                ? UiStrings.BarcodeSourceManual
                : string.Format(UiStrings.BarcodeOnPage, barcode.PageIndex + 1));
            var remove = C.Link("×", () =>
            {
                _document?.RemoveBarcode(current);
                BuildBarcodeRows();
                Refresh();
                _onChanged();
            });
            remove.ToolTip = UiStrings.RemoveBarcodeTooltip;
            _barcodes.Children.Add(
                L.Row(choice.Scale(), detail.AlignCenter(), remove.AlignCenter()).Spacing(6));
        }

        _ownValueChoice = first == null
            ? new RadioButton { Text = UiStrings.DocumentOwnValueOption }
            : new RadioButton(first) { Text = UiStrings.DocumentOwnValueOption };
        _ownValueChoice.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents || !_ownValueChoice!.Checked) return;
            _identifier.Focus();
        };
        _barcodes.Children.Add(_ownValueChoice);
    }

    /// <summary>
    /// Ticks whichever radio matches the document's current identification. A value the operator typed
    /// that is on none of the barcodes selects "own value" -- free text always wins, so the radios follow
    /// the identification rather than constraining it.
    /// </summary>
    private void SyncBarcodeSelection()
    {
        if (_document == null)
        {
            return;
        }
        var identifier = _document.Identifier;
        var matched = false;
        for (var i = 0; i < _barcodeChoices.Count && i < _document.Barcodes.Count; i++)
        {
            var isMatch = !matched && string.Equals(_document.Barcodes[i].Value, identifier,
                StringComparison.Ordinal);
            _barcodeChoices[i].Checked = isMatch;
            matched |= isMatch;
        }
        if (_ownValueChoice != null)
        {
            _ownValueChoice.Checked = !matched;
        }
    }
}
