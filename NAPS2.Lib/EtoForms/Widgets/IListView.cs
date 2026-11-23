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

    /// <summary>
    /// A section's heading was clicked, by index into the sections last given to
    /// <see cref="SetSections"/>. The heading stands for the whole document, so clicking it is how you
    /// take hold of all of its pages at once.
    /// </summary>
    event EventHandler<int> SectionClicked;

    void SetItems(IEnumerable<T> items);

    /// <summary>
    /// Groups the items into sections drawn under headings of their own, or ungroups them when given an
    /// empty list. Call it after every change to the items, since the sections address them by index.
    /// </summary>
    void SetSections(IReadOnlyList<ListViewSection> sections);

    void ApplyDiffs(ListViewDiffs<T> diffs);

    void RegenerateImages();

    /// <summary>
    /// Where the section headings read their motion from: for a document id, the opacity of its arrive
    /// marker and how much of the success colour its status icon still carries. A platform that draws
    /// no headings of its own can ignore it.
    /// </summary>
    void SetSectionMotion(Func<Guid, (float ArriveBar, float Confirmation)>? motion)
    {
    }

    /// <summary>Repaints the section headings, for a frame of their motion.</summary>
    void InvalidateSections()
    {
    }
}