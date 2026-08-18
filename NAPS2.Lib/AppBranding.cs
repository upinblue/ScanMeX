namespace NAPS2;

/// <summary>
/// The application's own name, and the window titles built from it.
/// </summary>
/// <remarks>
/// Deliberately a constant rather than a resource string. The title used to be assembled from
/// <c>UiStrings.Naps2TitleFormat</c>, whose neutral value the rebranding changed to "ScanMe - {0}" --
/// but the key exists in about forty inherited translation files, every one of which still reads
/// "NAPS2 - {0}", so the window came up branded NAPS2 for anyone not running the app in English. That
/// is also why it looked intermittent: German only ships in Release builds, so a Debug build showed the
/// corrected English string and looked fine.
///
/// A product name is not a translatable string, and routing it through the resources means every future
/// translation refresh from upstream can silently rebrand the window again.
/// </remarks>
public static class AppBranding
{
    public const string Name = "ScanMe";

    /// <summary>
    /// The company that publishes ScanMe and holds the copyright in it.
    /// </summary>
    public const string Company = "up in blue GmbH";

    /// <summary>
    /// ScanMe's own homepage.
    /// </summary>
    public const string HomepageUrl = "https://www.upinblue.com";

    /// <summary>
    /// The years ScanMe has been published in, which is what its copyright range covers.
    /// </summary>
    /// <remarks>
    /// The end year is a constant that the release tooling raises, not <c>DateTime.Now.Year</c>. A
    /// copyright notice states the years the work was published in, so a 2026 build still says 2026
    /// when it is run in 2029 -- reading the clock would have it claim years it was not published in,
    /// and would make the About box's text depend on when the operator happened to open it.
    /// </remarks>
    public const string CopyrightStartYear = "2025";

    public const string CopyrightEndYear = "2026";

    /// <summary>
    /// The name of the upstream project ScanMe is derived from, and its copyright line. Both are
    /// kept verbatim: the GPL lets a fork be sold and kept closed, but not published with the
    /// original authors' notices removed, and an About box is the one place in a shipped build where
    /// an operator can actually see who wrote what.
    /// </summary>
    public const string UpstreamName = "NAPS2";

    public const string UpstreamUrl = "https://www.naps2.com";

    public const string UpstreamCopyright = "Copyright 2009-2025 NAPS2 Contributors";

    /// <summary>
    /// The icon set the interface is drawn with, and its licence. See tools/icons/icon-map.tsv.
    /// </summary>
    public const string IconsName = "Fluent UI System Icons (MIT)";

    public const string IconsUrl = "https://github.com/microsoft/fluentui-system-icons";

    /// <summary>
    /// ScanMe's copyright line.
    /// </summary>
    /// <remarks>
    /// Deliberately not built from <c>UiStrings.CopyrightFormat</c>, for the reason described on this
    /// class: that key exists in 46 inherited translation files and 44 of them still read
    /// "NAPS2 Contributors", so the About box credited the wrong copyright holder in every language
    /// but English and German. A copyright notice is not a translatable string.
    /// </remarks>
    public static string Copyright =>
        CopyrightEndYear == CopyrightStartYear
            ? $"Copyright {CopyrightStartYear} {Company}"
            : $"Copyright {CopyrightStartYear}-{CopyrightEndYear} {Company}";

    /// <summary>
    /// The window title: the app's name, and what it is currently pointed at, if anything.
    /// </summary>
    public static string WindowTitle(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? Name : $"{Name} - {subject}";
}
