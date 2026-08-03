namespace NAPS2.EtoForms.Notifications;

/// <summary>
/// Reports the outcome of sending a scanned document to its target systems (SharePoint, SAP ArchiveLink),
/// so an upload is as visible to the operator as a successful save.
/// </summary>
public class UploadNotification : NotificationModel
{
    public UploadNotification(string title, string detail, bool isError)
    {
        Title = title;
        Detail = detail;
        IsError = isError;
    }

    public string Title { get; }

    public string Detail { get; }

    public bool IsError { get; }

    public override NotificationView CreateView()
    {
        return new UploadNotificationView(this);
    }
}
