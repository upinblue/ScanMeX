using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Widgets;

public abstract class ListViewBehavior<T> where T : notnull
{
    protected ListViewBehavior(ColorScheme colorScheme)
    {
        ColorScheme = colorScheme;
    }

    public ColorScheme ColorScheme { get; }

    public bool MultiSelect { get; protected set; }
        
    public bool ShowLabels { get; protected set; }

    public virtual bool ShowPageNumbers => false;

    public bool ScrollOnDrag { get; protected set; }

    public bool UseHandCursor { get; protected set; }

    public bool Checkboxes { get; protected set; }

    /// <summary>
    /// Whether this list is the app's document canvas rather than a list inside a dialog. The canvas
    /// gets <see cref="EtoForms.ColorScheme.CanvasColor"/> so the white pages on it read as objects;
    /// a list of profiles or devices wants the surrounding surface colour instead.
    /// </summary>
    public bool UseCanvasBackground { get; protected set; }

    /// <summary>
    /// What to draw when the list has no items, or null to leave it blank. This lives on the
    /// behavior rather than being overlaid as a control on purpose: an overlaid control owns a
    /// window, and a window in the middle of the list would swallow the file drops that land there.
    /// The platform list view draws it into its own background instead.
    /// </summary>
    public EmptyStateInfo? EmptyState { get; protected set; }

    /// <param name="IconName">Resolved through <see cref="IIconProvider"/> and drawn in the accent colour.</param>
    /// <param name="Title">One short line, sentence case.</param>
    /// <param name="Hint">What the user should do next.</param>
    public record EmptyStateInfo(string IconName, string Title, string Hint);

    public virtual string GetLabel(T item) => throw new NotSupportedException();

    public virtual Image GetImage(IListView<T> listView, T item) => throw new NotSupportedException();

    public virtual bool AllowDragDrop => false;

    public virtual bool AllowFileDrop => false;

    public virtual string CustomDragDataType => throw new NotSupportedException();

    public virtual byte[] SerializeCustomDragData(T[] items) => throw new NotSupportedException();

    public virtual byte[] MergeCustomDragData(byte[][] dataItems) => throw new NotSupportedException();

    public virtual DragEffects GetCustomDragEffect(byte[] data) => throw new NotSupportedException();
}