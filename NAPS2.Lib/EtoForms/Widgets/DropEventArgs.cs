using System.Collections.Immutable;

namespace NAPS2.EtoForms.Widgets;

public class DropEventArgs : EventArgs
{
    public DropEventArgs(int position, IEnumerable<string> filePaths, int anchorIndex = -1)
    {
        Position = position;
        FilePaths = filePaths.ToImmutableList();
        AnchorIndex = anchorIndex;
    }

    public DropEventArgs(int position, byte[] customData, int anchorIndex = -1)
    {
        Position = position;
        CustomData = customData;
        AnchorIndex = anchorIndex;
    }

    /// <summary>Where the dropped items are inserted.</summary>
    public int Position { get; }

    /// <summary>
    /// The item the pointer was over, or -1 when the list view does not report one.
    /// </summary>
    /// <remarks>
    /// The insert position cannot say which section a drop landed in, because the boundary between two
    /// of them is one number: "after the last page of this document" and "before the first page of the
    /// next" are the same index. The item under the pointer is what tells them apart, and it is what the
    /// operator is looking at -- so a page dropped on the left half of a document's first page joins
    /// that document, and one dropped on the right half of the page above it joins the document above.
    /// </remarks>
    public int AnchorIndex { get; }

    public ImmutableList<string>? FilePaths { get; }

    public byte[]? CustomData { get; }
}
