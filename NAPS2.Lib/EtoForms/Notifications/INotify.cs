using NAPS2.Update;

namespace NAPS2.EtoForms.Notifications;

public interface INotify : ISaveNotify
{
    /// <summary>
    /// Something the operator asked for was not done, and why. An edit that quietly does nothing is the
    /// failure this app exists to make visible, so a refusal has to say so.
    /// </summary>
    void Refused(string title, string detail);

    void DonatePrompt();
    void ReviewPrompt();
    void OperationProgress(OperationProgress progress, IOperation op);
    void UpdateAvailable(IUpdateChecker updateChecker, UpdateInfo update);
}