namespace NAPS2.EtoForms.Notifications;

public class SaveNotifyStub : ISaveNotify
{
    public void PdfSaved(string path)
    {
    }

    public void ImagesSaved(int imageCount, string path)
    {
    }

    public void DocumentUploaded(string fileName, string targets)
    {
    }

    public void DocumentUploadFailed(string fileName, string message)
    {
    }
}