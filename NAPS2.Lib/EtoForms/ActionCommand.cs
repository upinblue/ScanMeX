using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms;

public class ActionCommand : Command
{
    public ActionCommand()
    {
    }

    public ActionCommand(Action action) : base((sender, args) => action())
    {
    }

    public ActionCommand(Func<Task> action) : base(async (sender, args) => await action())
    {
    }

    public string Text
    {
        get => string.IsNullOrEmpty(ToolBarText) ? MenuText : ToolBarText;
        set
        {
            ToolBarText = value;
            MenuText = value;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? TextChanged;

    /// <summary>
    /// A short sentence shown as the toolbar item's tooltip. Its main use is saying why a command is
    /// disabled right now: a greyed-out button with no way to learn why is indistinguishable from a
    /// broken one. Null shows no tooltip.
    /// </summary>
    public string? Hint
    {
        get => _hint;
        set
        {
            if (_hint == value) return;
            _hint = value;
            HintChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? _hint;

    public event EventHandler? HintChanged;

    public string? IconName { get; set; }

    public Image? GetIconImage(float scale) =>
        IconName != null ? EtoPlatform.Current.IconProvider.GetIcon(IconName, scale) : null;
}