namespace NAPS2.EtoForms;

[Flags]
public enum ButtonFlags
{
    // The values are explicit because these are flags: a member added without one would take the
    // next ordinal, and the fourth member would silently be LargeIcon | LargeText.
    None = 0,
    LargeIcon = 1,
    LargeText = 2,

    /// <summary>
    /// Fluent's accent button: filled with the accent colour instead of outlined. It marks the one
    /// primary action on a surface, so a layout with two of them has picked the wrong one.
    /// </summary>
    Accent = 4
}
