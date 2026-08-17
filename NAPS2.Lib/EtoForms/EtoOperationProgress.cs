using Eto.Forms;
using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Ui;
using NAPS2.EtoForms.Widgets;

namespace NAPS2.EtoForms;

public class EtoOperationProgress : OperationProgress
{
    private readonly IFormFactory _formFactory;
    private readonly INotify _notify;
    private readonly Naps2Config _config;

    private readonly HashSet<IOperation> _activeOperations = [];

    public EtoOperationProgress(IFormFactory formFactory, INotify notify, Naps2Config config)
    {
        _formFactory = formFactory;
        _notify = notify;
        _config = config;
    }

    /// <remarks>
    /// Every access to the set takes the same lock. It used to add under <c>lock (this)</c>, read under
    /// <c>lock (_activeOperations)</c> and remove -- from the operation's Finished event, which is raised
    /// on whichever background thread the upload or save ran on -- under no lock at all. A batch produces
    /// one operation per document, so a document finishing while the notification manager was enumerating
    /// the set is not a rare interleaving; it is what a normal multi-document scan does, and a HashSet
    /// mutated during enumeration throws.
    /// </remarks>
    public override void Attach(IOperation op)
    {
        lock (_activeOperations)
        {
            if (!_activeOperations.Add(op))
            {
                return;
            }
        }
        op.Finished += (_, _) => Detach(op);
        // Checked after subscribing, not before: an operation that finishes in between would otherwise
        // raise Finished before the handler existed and stay in the set for the life of the app.
        if (op.IsFinished)
        {
            Detach(op);
        }
    }

    private void Detach(IOperation op)
    {
        lock (_activeOperations)
        {
            _activeOperations.Remove(op);
        }
    }

    public override void ShowProgress(IOperation op)
    {
        if (PlatformCompat.System.ShouldRememberBackgroundOperations &&
            _config.Get(c => c.BackgroundOperations).Contains(op.GetType().Name))
        {
            ShowBackgroundProgress(op);
        }
        else
        {
            ShowModalProgress(op);
        }
    }

    public override void ShowModalProgress(IOperation op)
    {
        Attach(op);

        if (!op.IsFinished)
        {
            Invoker.Current.Invoke(() =>
            {
                var form = _formFactory.Create<ProgressForm>();
                form.Operation = op;
                form.ShowModal();
            });
        }

        if (!op.IsFinished)
        {
            ShowBackgroundProgress(op);
        }
    }

    public override void ShowBackgroundProgress(IOperation op)
    {
        Attach(op);

        if (!op.IsFinished)
        {
            Invoker.Current.Invoke(() => _notify.OperationProgress(this, op));
        }
    }

    public static void RenderStatus(IOperation op, Label textLabel, Label numberLabel,
        FluentProgressBar progressBar)
    {
        var status = op.Status ?? new OperationStatus();
        textLabel.Text = status.StatusText;
        // An operation that cannot say how much there is to do is indeterminate, whether it says so
        // outright or by reporting no total at all. ImportOperation sets MaxProgress to 0 for a single
        // file precisely because it has no page count yet, and that used to draw a determinate bar sitting
        // at empty for the whole import -- which is the "step that quietly does nothing" this app exists
        // to make visible, not a report of progress. MaxProgress == 1 is the same case from the other
        // side: a bar with one step only ever reads as empty or full.
        progressBar.Indeterminate = status.MaxProgress <= 1 || status.IndeterminateProgress;
        if (status.MaxProgress <= 1 || status.ProgressType == OperationProgressType.None)
        {
            numberLabel.Text = "";
        }
        else if (status.ProgressType == OperationProgressType.BarOnly)
        {
            numberLabel.Text = "";
            progressBar.MaxValue = status.MaxProgress;
            progressBar.Value = status.CurrentProgress;
        }
        else
        {
            numberLabel.Text = status.ProgressType == OperationProgressType.MB
                ? string.Format(MiscResources.SizeProgress, (status.CurrentProgress / 1000000.0).ToString("f1"),
                    (status.MaxProgress / 1000000.0).ToString("f1"))
                : string.Format(MiscResources.ProgressFormat, status.CurrentProgress, status.MaxProgress);
            progressBar.MaxValue = status.MaxProgress;
            progressBar.Value = status.CurrentProgress;
        }
        // The nudge that used to be here -- value += 1; value -= 1 -- existed to defeat the native
        // control's own easing animation, which lagged behind the value it had been given. FluentProgressBar
        // owns its animation and repaints from the value it was last set to, so there is nothing to force.
    }

    public override List<IOperation> ActiveOperations
    {
        get
        {
            lock (_activeOperations)
            {
                return _activeOperations.ToList();
            }
        }
    }
}