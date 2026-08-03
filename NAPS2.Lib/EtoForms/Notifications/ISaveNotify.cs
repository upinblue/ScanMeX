namespace NAPS2.EtoForms.Notifications;

/// <summary>
/// A base interface for objects that can display information about saved files to the user.
/// </summary>
public interface ISaveNotify
{
    /// <summary>
    /// Indicate that a PDF file has been saved.
    /// </summary>
    /// <param name="path"></param>
    void PdfSaved(string path);

    /// <summary>
    /// Indicate that one or more image files have been saved.
    /// </summary>
    /// <param name="imageCount"></param>
    /// <param name="path"></param>
    void ImagesSaved(int imageCount, string path);

    /// <summary>
    /// Indicate that a document reached all of the target systems its profile enables.
    /// </summary>
    /// <param name="fileName">The document's file name.</param>
    /// <param name="targets">The target systems it was sent to, already formatted for display.</param>
    void DocumentUploaded(string fileName, string targets);

    /// <summary>
    /// Indicate that a document could not be sent to at least one of its target systems.
    /// </summary>
    /// <param name="fileName">The document's file name.</param>
    /// <param name="message">Why it failed, including which target system reported the problem.</param>
    void DocumentUploadFailed(string fileName, string message);
}