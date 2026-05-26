using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.Sap;

namespace NAPS2.EtoForms.Ui;

internal class SapObjectKeyPromptForm : EtoDialogBase
{
    private readonly TextBox _objectKey = new();
    private readonly string _fileName;

    public SapObjectKeyPromptForm(Naps2Config config, string fileName, string? suggestedValue = null) : base(config)
    {
        _fileName = fileName;
        _objectKey.Text = suggestedValue ?? "";
        Title = SapUi.ObjectKeyPromptTitle;
        FormStateController.FixedHeightLayout = true;
    }

    public string? ObjectKey { get; private set; }

    protected override void BuildLayout()
    {
        LayoutController.Content = L.Column(
            C.Label(SapUi.ObjectKeyPrompt),
            C.Label(_fileName),
            _objectKey,
            C.Filler(),
            L.Row(
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Save),
                    C.CancelButton(this)))
        );
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_objectKey.Text))
        {
            _objectKey.Focus();
            return false;
        }
        ObjectKey = _objectKey.Text.Trim();
        return true;
    }
}
