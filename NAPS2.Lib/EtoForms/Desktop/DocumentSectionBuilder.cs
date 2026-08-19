using NAPS2.EtoForms.Widgets;
using NAPS2.Images;
using NAPS2.PostScan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// Turns the pages in the window into the sections the canvas draws: one per document, in the order the
/// pages are in, plus one for the pages that belong to no document.
/// </summary>
/// <remarks>
/// Deliberately free of any list view: which pages form a section is a question about documents, and it
/// is the one part of the sectioned canvas that can be checked without a window.
/// </remarks>
public class DocumentSectionBuilder
{
    private readonly DocumentPageTracker _pageTracker;
    private readonly ColorScheme _colorScheme;

    public DocumentSectionBuilder(DocumentPageTracker pageTracker, ColorScheme colorScheme)
    {
        _pageTracker = pageTracker;
        _colorScheme = colorScheme;
    }

    /// <summary>
    /// The sections for the given pages, in their order. Empty when no page belongs to a document at
    /// all -- a window holding only imported pages is not a document that failed to be recognised, and
    /// heading it "unassigned" would be an accusation rather than information.
    /// </summary>
    public IReadOnlyList<ListViewSection> Build(IReadOnlyList<UiImage> pages)
    {
        var owners = pages.Select(_pageTracker.DocumentFor).ToList();
        if (owners.All(x => x == null))
        {
            return [];
        }

        var sections = new List<ListViewSection>();
        var start = 0;
        while (start < pages.Count)
        {
            var owner = owners[start];
            var end = start;
            // A document is a run of consecutive pages, so the section ends where the owner changes.
            while (end + 1 < pages.Count && ReferenceEquals(owners[end + 1], owner))
            {
                end++;
            }
            sections.Add(Describe(owner, start, end - start + 1));
            start = end + 1;
        }
        return sections;
    }

    private ListViewSection Describe(ScannedDocument? document, int startIndex, int count)
    {
        var pageCount = string.Format(UiStrings.DocumentPageCount, count);
        if (document == null)
        {
            return new ListViewSection(UiStrings.SectionUnassigned, pageCount,
                _colorScheme.SecondaryTextColor, startIndex, count);
        }
        var severity = DocumentInspector.SeverityOf(document.Status);
        var color = severity == Notifications.NotificationSeverity.Neutral
            ? _colorScheme.SecondaryTextColor
            : _colorScheme.GetSeverityColor(severity);
        var title = DocumentInspector.ResolveName(document) ?? UiStrings.DocumentNameMissingShort;
        var meta = $"{pageCount}  ·  {DocumentInspector.DescribeStatus(document)}";
        return new ListViewSection(title, meta, color, startIndex, count);
    }
}
