using Eto.Forms;

namespace NAPS2.EtoForms.Widgets;

public interface IListView<T> : Util.ISelectable<T> where T : notnull
{
    Control Control { get; }

    ContextMenu? ContextMenu { get; set; }

    Eto.Drawing.Size ImageSize { get; set; }

    event EventHandler SelectionChanged;

    event EventHandler ItemClicked;

    event EventHandler<DropEventArgs> Drop;

    void SetItems(IEnumerable<T> items);

    /// <summary>
    /// Groups the items into sections drawn under headings of their own, or ungroups them when given an
    /// empty list. Call it after every change to the items, since the sections address them by index.
    /// </summary>
    void SetSections(IReadOnlyList<ListViewSection> sections);

    void ApplyDiffs(ListViewDiffs<T> diffs);

    void RegenerateImages();
}