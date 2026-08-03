using NAPS2.Update;

namespace NAPS2.EtoForms.Notifications;

public class StubNotify : INotify
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

    public void DonatePrompt()
    {
    }

    public void ReviewPrompt()
    {
    }

    public void OperationProgress(OperationProgress progress, IOperation op)
    {
    }

    public void UpdateAvailable(IUpdateChecker updateChecker, UpdateInfo update)
    {
    }
}