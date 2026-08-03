using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Scan;
using System.Text.RegularExpressions;
using Eto.Drawing;

namespace NAPS2.EtoForms.Ui;

public class AutoSaveSettingsForm : EtoDialogBase
{
    private const string PATCH_CODE_INFO_URL = "https://www.naps2.com/doc/batch-scan#patch-t";

    private readonly FilePathWithPlaceholders _filePath;
    private readonly CheckBox _promptForFilePath = new() { Text = UiStrings.PromptForFilePath };
    private readonly RadioButton _filePerPage;
    private readonly RadioButton _filePerScan;
    private readonly RadioButton _separateByPatchT;
    private readonly RadioButton _separateByCode39;
    private readonly Label _code39RegexLabel = C.Label(UiStrings.SeparationPatternLabel);
    private readonly TextBox _code39Regex = new();
    private readonly LayoutVisibility _code39RegexVis = new(false);
    private readonly CheckBox _clearAfterSaving = new() { Text = UiStrings.ClearAfterSaving };
    // Which targets are enabled is decided in the profile dialog next to their credentials; this dialog
    // only controls when the upload happens.
    private readonly Label _uploadTargetsInfo = C.Label("");

    // Barcode separation details, only relevant when separating by barcode.
    private readonly CheckBox _symbologyCode39 = new() { Text = UiStrings.BarcodeTypeCode39 };
    private readonly CheckBox _symbologyCode128 = new() { Text = UiStrings.BarcodeTypeCode128 };
    private readonly CheckBox _symbologyEanUpc = new() { Text = UiStrings.BarcodeTypeEanUpc };
    private readonly CheckBox _keepSeparatorPage = new() { Text = UiStrings.KeepSeparatorPage };
    private readonly LayoutVisibility _barcodeOptionsVis = new(false);

    // How each document is identified and when it gets uploaded.
    private readonly DropDownWidget<DocumentIdMode> _idMode = new();
    private readonly TextBox _idPromptLabel = new();
    private readonly LayoutVisibility _idPromptLabelVis = new(false);
    private readonly DropDownWidget<UploadTrigger> _uploadTrigger = new();
    private readonly CheckBox _keepLocalCopy = new() { Text = UiStrings.KeepLocalCopy };
    private readonly CheckBox _cleanupAfterCompletion = new() { Text = UiStrings.CleanupAfterCompletion };

    public AutoSaveSettingsForm(Naps2Config config, DialogHelper dialogHelper)
        : base(config)
    {
        _filePath = new(this, dialogHelper);
        _filePerPage = new() { Text = UiStrings.OneFilePerPage, Checked = true };
        _filePerScan = new(_filePerPage) { Text = UiStrings.OneFilePerScan };
        _separateByPatchT = new RadioButton(_filePerPage) { Text = UiStrings.SeparationModePatchT };
        _separateByCode39 = new RadioButton(_filePerPage) { Text = UiStrings.SeparationModeBarcode };

        // Make regex textbox large enough
        _code39Regex.Size = new Size(320, -1);
        _idPromptLabel.Size = new Size(320, -1);

        _idMode.Format = x => x switch
        {
            DocumentIdMode.Barcode => UiStrings.DocumentIdModeBarcode,
            DocumentIdMode.ManualInput => UiStrings.DocumentIdModeManual,
            _ => UiStrings.DocumentIdModeNone
        };
        _idMode.Items = EnumDropDownWidget<DocumentIdMode>.DefaultItems;
        _idMode.SelectedItemChanged += (_, _) => UpdateIdPromptVisibility();

        _uploadTrigger.Format = x => x == UploadTrigger.Manual
            ? UiStrings.UploadTriggerManual
            : UiStrings.UploadTriggerAutomatic;
        _uploadTrigger.Items = EnumDropDownWidget<UploadTrigger>.DefaultItems;

        _separateByPatchT.CheckedChanged += SeparationOption_CheckedChanged;
        _separateByCode39.CheckedChanged += SeparationOption_CheckedChanged;
        _filePerPage.CheckedChanged += SeparationOption_CheckedChanged;
        _filePerScan.CheckedChanged += SeparationOption_CheckedChanged;
    }

    public ScanProfile? ScanProfile { get; set; }

    public bool Result { get; private set; }

    protected override void BuildLayout()
    {
        if (ScanProfile?.AutoSaveSettings != null)
        {
            _filePath.Text = ScanProfile.AutoSaveSettings.FilePath;
            _promptForFilePath.Checked = ScanProfile.AutoSaveSettings.PromptForFilePath;
            _clearAfterSaving.Checked = ScanProfile.AutoSaveSettings.ClearImagesAfterSaving;
            if (ScanProfile.AutoSaveSettings.Separator == SaveSeparator.FilePerScan)
            {
                _filePerScan.Checked = true;
            }
            else if (ScanProfile.AutoSaveSettings.Separator == SaveSeparator.PatchT)
            {
                _separateByPatchT.Checked = true;
            }
            else if (ScanProfile.AutoSaveSettings.Separator == SaveSeparator.Code39Barcode)
            {
                _separateByCode39.Checked = true;
            }
            else
            {
                _filePerPage.Checked = true;
            }
            _code39Regex.Text = ScanProfile.AutoSaveSettings.Code39SeparationPattern ?? "";
        }
        _uploadTargetsInfo.Text = DescribeUploadTargets();

        // Falls back to values derived from the legacy auto save settings for older profiles.
        var workflow = DocumentWorkflowSettings.ForProfile(ScanProfile);
        if (workflow.SeparationMode == DocumentSeparationMode.Barcode)
        {
            _separateByCode39.Checked = true;
        }
        _code39Regex.Text = workflow.SeparationPattern ?? _code39Regex.Text;
        _symbologyCode39.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.Code39);
        _symbologyCode128.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.Code128);
        _symbologyEanUpc.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.EanUpc);
        _keepSeparatorPage.Checked = workflow.KeepSeparatorPage;
        _idMode.SelectedItem = workflow.IdMode;
        _idPromptLabel.Text = workflow.IdPromptLabel ?? "";
        _uploadTrigger.SelectedItem = workflow.UploadTrigger;
        _keepLocalCopy.Checked = workflow.KeepLocalCopy;
        _cleanupAfterCompletion.Checked = workflow.CleanupAfterCompletion;

        Title = UiStrings.AutoSaveSettingsFormTitle;

        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            C.Label(UiStrings.FilePathLabel).NaturalWidth(300),
            _filePath,
            _promptForFilePath,
            C.Spacer(),
            C.Spacer(),
            C.Label(UiStrings.SeparationModeLabel),
            _filePerPage,
            _filePerScan,
            _separateByPatchT,
            _separateByCode39,
            L.Column(
                C.Label(UiStrings.BarcodeTypesLabel),
                L.Row(_symbologyCode39, _symbologyCode128, _symbologyEanUpc),
                _keepSeparatorPage
            ).Visible(_barcodeOptionsVis),
            L.Column(
                _code39RegexLabel,
                _code39Regex,
                C.Label(UiStrings.SeparationPatternHint)
            ).Visible(_code39RegexVis),
            C.UrlLink(PATCH_CODE_INFO_URL, UiStrings.MoreInfo),
            C.Spacer(),
            C.Spacer(),
            C.Label(UiStrings.DocumentIdModeLabel),
            _idMode,
            L.Column(C.Label(UiStrings.DocumentIdPromptLabelLabel), _idPromptLabel).Visible(_idPromptLabelVis),
            C.Spacer(),
            C.Label(UiStrings.UploadTriggerLabel),
            _uploadTrigger,
            _uploadTargetsInfo,
            _keepLocalCopy,
            C.Spacer(),
            _clearAfterSaving,
            _cleanupAfterCompletion,
            C.Filler(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Save),
                    C.CancelButton(this))
            )
        );

        UpdateRegexVisibility();
        UpdateIdPromptVisibility();
        UpdateUploadCheckboxEnabled();
    }

    private void SeparationOption_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateRegexVisibility();
    }

    private void UpdateRegexVisibility()
    {
        // The pattern and the symbology selection only apply to barcode separation.
        bool show = _separateByCode39.Checked;
        _code39RegexVis.IsVisible = show;
        _barcodeOptionsVis.IsVisible = show;
        _code39Regex.Enabled = show;
        _code39RegexLabel.Enabled = show;
    }

    private void UpdateIdPromptVisibility()
    {
        _idPromptLabelVis.IsVisible = _idMode.SelectedItem == DocumentIdMode.ManualInput;
    }

    // The upload timing only matters when auto save actually produces a file to upload.
    private void UpdateUploadCheckboxEnabled()
    {
        bool enabled = ScanProfile?.EnableAutoSave == true;
        _uploadTrigger.Enabled = enabled;
        _keepLocalCopy.Enabled = enabled;
    }

    /// <summary>
    /// Names the targets enabled in the profile dialog, so it's clear here what "upload" refers to.
    /// </summary>
    private string DescribeUploadTargets()
    {
        var targets = new List<string>();
        if (ScanProfile?.UploadsToSharePoint() == true)
        {
            targets.Add(UiStrings.SharePointUpload);
        }
        if (ScanProfile?.UploadsToSap() == true)
        {
            targets.Add(UiStrings.SapArchiveLink);
        }
        return targets.Count > 0
            ? string.Format(UiStrings.UploadTargetsEnabled, string.Join(", ", targets))
            : UiStrings.UploadTargetsNone;
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_filePath.Text) && !_promptForFilePath.IsChecked())
        {
            _filePath.Focus();
            return false;
        }
        var separator = _filePerScan.Checked ? SaveSeparator.FilePerScan
            : _separateByPatchT.Checked ? SaveSeparator.PatchT
            : _separateByCode39.Checked ? SaveSeparator.Code39Barcode
            : SaveSeparator.FilePerPage;

        // Minimal regex validation when barcode separation is selected and the pattern is non-empty
        string? regex = null;
        if (separator == SaveSeparator.Code39Barcode)
        {
            var text = _code39Regex.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    _ = new Regex(text);
                    regex = text;
                }
                catch (Exception)
                {
                    MessageBox.Show(this, UiStrings.InvalidSeparationPattern, MessageBoxType.Error);
                    _code39Regex.Focus();
                    return false;
                }
            }
        }

        ScanProfile!.AutoSaveSettings = new AutoSaveSettings
        {
            FilePath = _filePath.Text!,
            PromptForFilePath = _promptForFilePath.IsChecked(),
            ClearImagesAfterSaving = _clearAfterSaving.IsChecked(),
            Separator = separator,
            Code39SeparationPattern = regex,
            // Owned by the profile dialog; preserved here so saving this dialog can't turn uploads off.
            UploadToSharePoint = ScanProfile.AutoSaveSettings?.UploadToSharePoint ?? false,
            UploadToSap = ScanProfile.AutoSaveSettings?.UploadToSap ?? false
        };

        var symbologies = new List<BarcodeSymbology>();
        if (_symbologyCode39.IsChecked()) symbologies.Add(BarcodeSymbology.Code39);
        if (_symbologyCode128.IsChecked()) symbologies.Add(BarcodeSymbology.Code128);
        if (_symbologyEanUpc.IsChecked()) symbologies.Add(BarcodeSymbology.EanUpc);

        ScanProfile.DocumentWorkflow = new DocumentWorkflowSettings
        {
            SeparationMode = _separateByCode39.Checked ? DocumentSeparationMode.Barcode
                : _separateByPatchT.Checked ? DocumentSeparationMode.PatchT
                : DocumentSeparationMode.None,
            BarcodeSymbologies = symbologies,
            SeparationPattern = regex,
            KeepSeparatorPage = _keepSeparatorPage.IsChecked(),
            IdMode = _idMode.SelectedItem,
            IdPromptLabel = string.IsNullOrWhiteSpace(_idPromptLabel.Text) ? null : _idPromptLabel.Text!.Trim(),
            UploadTrigger = _uploadTrigger.SelectedItem,
            KeepLocalCopy = _keepLocalCopy.IsChecked(),
            CleanupAfterCompletion = _cleanupAfterCompletion.IsChecked()
        };
        Result = true;
        return true;
    }
}