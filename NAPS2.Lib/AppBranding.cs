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
    /// The window title: the app's name, and what it is currently pointed at, if anything.
    /// </summary>
    public static string WindowTitle(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? Name : $"{Name} - {subject}";
}
