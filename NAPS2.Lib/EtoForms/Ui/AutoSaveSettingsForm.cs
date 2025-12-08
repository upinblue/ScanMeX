using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport;
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
    private readonly Label _code39RegexLabel = C.Label("Code 39 regex (optional):");
    private readonly TextBox _code39Regex = new();
    private readonly LayoutVisibility _code39RegexVis = new(false);
    private readonly CheckBox _clearAfterSaving = new() { Text = UiStrings.ClearAfterSaving };
    // New: Upload to SharePoint toggle for auto-saved document
    private readonly CheckBox _uploadToSharePoint = new() { Text = "Upload auto-saved document to SharePoint" };

    public AutoSaveSettingsForm(Naps2Config config, DialogHelper dialogHelper)
        : base(config)
    {
        _filePath = new(this, dialogHelper);
        _filePerPage = new() { Text = UiStrings.OneFilePerPage, Checked = true };
        _filePerScan = new(_filePerPage) { Text = UiStrings.OneFilePerScan };
        _separateByPatchT = new RadioButton(_filePerPage) { Text = UiStrings.SeparateByPatchT };
        _separateByCode39 = new RadioButton(_filePerPage) { Text = "Separate by Code 39 barcode" };

        // Make regex textbox large enough
        _code39Regex.Size = new Size(320, -1);

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
            // Initialize upload checkbox from model
            _uploadToSharePoint.Checked = ScanProfile.AutoSaveSettings.UploadToSharePoint;
        }

        Title = UiStrings.AutoSaveSettingsFormTitle;

        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            C.Label(UiStrings.FilePathLabel).NaturalWidth(300),
            _filePath,
            _promptForFilePath,
            C.Spacer(),
            C.Spacer(),
            _filePerPage,
            _filePerScan,
            _separateByPatchT,
            _separateByCode39,
            L.Column(_code39RegexLabel, _code39Regex).Visible(_code39RegexVis),
            C.UrlLink(PATCH_CODE_INFO_URL, UiStrings.MoreInfo),
            C.Spacer(),
            C.Spacer(),
            _clearAfterSaving,
            // New checkbox for uploading to SharePoint
            _uploadToSharePoint,
            C.Filler(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Save),
                    C.CancelButton(this))
            )
        );

        UpdateRegexVisibility();
        UpdateUploadCheckboxEnabled();
    }

    private void SeparationOption_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateRegexVisibility();
    }

    private void UpdateRegexVisibility()
    {
        bool show = _separateByCode39.Checked;
        _code39RegexVis.IsVisible = show;
        _code39Regex.Enabled = show;
        _code39RegexLabel.Enabled = show;
    }

    // Ensure the upload checkbox is enabled only when Auto Save is enabled in the parent form/profile
    private void UpdateUploadCheckboxEnabled()
    {
        bool enabled = ScanProfile?.EnableAutoSave == true;
        _uploadToSharePoint.Enabled = enabled;
        if (!enabled)
        {
            _uploadToSharePoint.Checked = false;
        }
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

        // Minimal regex validation when Code39 selected and non-empty
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
                    MessageBox.Show(this, "Invalid Code 39 regex.", MessageBoxType.Error);
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
            UploadToSharePoint = _uploadToSharePoint.IsChecked()
        };
        Result = true;
        return true;
    }
}