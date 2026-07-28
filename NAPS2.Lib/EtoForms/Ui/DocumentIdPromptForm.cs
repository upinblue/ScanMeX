using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Asks the operator for the identification number of one scanned document. Shown once per document
/// after scanning has finished, so the feeder never waits for input.
/// </summary>
public class DocumentIdPromptForm : EtoDialogBase
{
    private readonly TextBox _documentId = new();
    private readonly string _description;
    private readonly string _promptLabel;

    public DocumentIdPromptForm(Naps2Config config, string description, string? promptLabel = null,
        string? suggestedValue = null) : base(config)
    {
        _description = description;
        _promptLabel = string.IsNullOrWhiteSpace(promptLabel) ? UiStrings.DocumentIdPrompt : promptLabel!;
        _documentId.Text = suggestedValue ?? "";
        _documentId.Size = new Size(280, -1);
        Title = UiStrings.DocumentIdPromptTitle;
        FormStateController.FixedHeightLayout = true;
    }

    public string? DocumentId { get; private set; }

    /// <summary>
    /// Whether the operator skipped this document rather than entering a value.
    /// </summary>
    public bool Skipped { get; private set; }

    protected override void BuildLayout()
    {
        LayoutController.Content = L.Column(
            C.Label(_promptLabel),
            C.Label(_description),
            _documentId,
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
        if (string.IsNullOrWhiteSpace(_documentId.Text))
        {
            _documentId.Focus();
            return false;
        }
        DocumentId = _documentId.Text.Trim();
        Skipped = false;
        return true;
    }
}
