using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Ui;

public class AboutForm : EtoDialogBase
{
    private const string NAPS2_HOMEPAGE = "https://www.upinblue.com";
    private const string ICONS_HOMEPAGE = "https://www.fatcow.com/free-icons";

    private readonly CheckBox _enableDebugLogging = C.CheckBox(UiStrings.EnableDebugLogging);

    public AboutForm(Naps2Config config, ScanningContext scanningContext)
        : base(config)
    {
        Title = UiStrings.AboutFormTitle;
        IconName = "information_small";

        _enableDebugLogging.Checked = config.Get(c => c.EnableDebugLogging);
        _enableDebugLogging.CheckedChanged += (_, _) =>
        {
            config.User.Set(c => c.EnableDebugLogging, _enableDebugLogging.IsChecked());
            NLogConfig.EnvDebugLogging = _enableDebugLogging.IsChecked();
            scanningContext.WorkerFactory?.RecreateSpareWorkers();
        };
    }

    protected override void BuildLayout()
    {
        FormStateController.Resizable = false;
        FormStateController.RestoreFormState = false;

        LayoutController.DefaultSpacing = 2;
        LayoutController.Content = L.Row(
            L.Column(new ImageView { Image = Icons.scanner_128.ToEtoImage() }).Padding(right: 4),
            L.Column(
                C.NoWrap(AssemblyHelper.Product),
                L.Row(
                    L.Column(
                        C.NoWrap(string.Format(MiscResources.Version, AssemblyHelper.Version)),
                        C.UrlLink(NAPS2_HOMEPAGE)
                    ),
                    C.None()
                ),
                C.TextSpace(),
                C.NoWrap(string.Format(UiStrings.CopyrightFormat, AssemblyHelper.COPYRIGHT_YEARS)),
                Config.AppLocked.Has(c => c.EnableDebugLogging)
                    ? C.None()
                    : new[] { C.Spacer(), _enableDebugLogging.Padding(left: 4) }.Expand(),
                C.TextSpace(),
                L.Row(
                    L.Column(
                        C.NoWrap(UiStrings.IconsFrom),
                        C.UrlLink(ICONS_HOMEPAGE)
                    ).Scale(),
                    L.Column(
                        C.Filler(),
                        C.DialogButton(this, UiStrings.OK, true, true)
                    ).Padding(left: 20)
                )
            )
        );
    }
}