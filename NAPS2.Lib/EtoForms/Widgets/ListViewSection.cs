using Eto.Drawing;

namespace NAPS2.EtoForms.Widgets;

/// <summary>
/// A run of consecutive items in a list view drawn under a heading of its own -- on the page canvas, the
/// pages that make up one document.
/// </summary>
/// <remarks>
/// A range rather than a set of items on purpose: a document is a run of consecutive pages, so a section
/// can never be interleaved with another one and the flat index of an item still says where it is on the
/// screen. Everything that addresses pages by position -- moving them, the drop position, the selection
/// -- therefore keeps working unchanged.
/// </remarks>
/// <param name="Title">The name of the document, or what stands in for it.</param>
/// <param name="Meta">The second, quieter line: page count, status, where it is going.</param>
/// <param name="Color">The status colour, drawn as a dot in front of the title.</param>
/// <param name="StartIndex">The index of the section's first item.</param>
/// <param name="Count">How many items are in it.</param>
public record ListViewSection(string Title, string Meta, Color Color, int StartIndex, int Count)
{
    public int EndIndex => StartIndex + Count - 1;

    public bool Contains(int index) => index >= StartIndex && index <= EndIndex;
}
