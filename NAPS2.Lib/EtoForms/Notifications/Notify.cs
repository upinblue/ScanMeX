using NAPS2.Update;

namespace NAPS2.EtoForms.Notifications;

public class Notify : INotify
{
    private readonly NotificationManager _notificationManager;

    public Notify(NotificationManager notificationManager)
    {
        _notificationManager = notificationManager;
    }

    public void PdfSaved(string path)
    {
        _notificationManager.Show(new SaveNotification(MiscResources.PdfSaved, path));
    }

    public void ImagesSaved(int imageCount, string path)
    {
        var title = imageCount == 1
            ? MiscResources.ImageSaved
            : string.Format(MiscResources.ImagesSaved, imageCount);
        _notificationManager.Show(new SaveNotification(title, path));
    }

    public void DocumentUploaded(string fileName, string targets)
    {
        _notificationManager.Show(new UploadNotification(
            string.Format(UiStrings.UploadNotificationSucceeded, targets), fileName, false));
    }

    public void DocumentUploadFailed(string fileName, string message)
    {
        _notificationManager.Show(new UploadNotification(
            string.Format(UiStrings.UploadNotificationFailed, fileName), message, true));
    }

    public void DonatePrompt()
    {
        _notificationManager.Show(new DonateNotification());
    }

    public void ReviewPrompt()
    {
        _notificationManager.Show(new ReviewNotification());
    }

    public void OperationProgress(OperationProgress progress, IOperation op)
    {
        _notificationManager.Show(new ProgressNotification(progress, op));
    }

    public void UpdateAvailable(IUpdateChecker updateChecker, UpdateInfo update)
    {
        _notificationManager.Show(new UpdateNotification(updateChecker, update));
    }
}