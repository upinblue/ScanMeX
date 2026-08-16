namespace NAPS2.EtoForms.Notifications;

/// <summary>
/// The severity levels of Fluent's InfoBar, which is what a notification is styled after. The colour
/// is the message: an operator scanning a stack of documents reads the tint before the text, so an
/// upload that reached neither SharePoint nor SAP must not look like one that reached both.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Plain card, no tint. The default for anything that isn't reporting an outcome.</summary>
    Neutral,
    Success,
    Warning,
    Error
}
