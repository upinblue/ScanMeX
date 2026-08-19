namespace NAPS2.EtoForms.Notifications;

/// <summary>
/// A short message with an outcome to it: an upload that reached its targets or failed to, an edit the
/// window refused. The severity is the message as much as the text is -- an operator working through a
/// stack reads the tint before the words.
/// </summary>
public class MessageNotification : NotificationModel
{
    public MessageNotification(string title, string detail, NotificationSeverity severity)
    {
        Title = title;
        Detail = detail;
        Severity = severity;
    }

    public string Title { get; }

    public string Detail { get; }

    public NotificationSeverity Severity { get; }

    public override NotificationView CreateView()
    {
        return new MessageNotificationView(this);
    }
}
