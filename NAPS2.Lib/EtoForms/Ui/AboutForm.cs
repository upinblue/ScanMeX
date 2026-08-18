using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// The About dialog: what this program is, who publishes it, and what it is built on.
/// </summary>
/// <remarks>
/// It reads as up in blue's product, and credits NAPS2 as the work it is derived from. Both halves are
/// deliberate. It used to show upstream's flatbed-scanner illustration, credit FatCow for icons the app
/// stopped using, and print a copyright line assembled from <c>UiStrings.CopyrightFormat</c> -- a key
/// that exists in 46 inherited translation files, 44 of which still name the NAPS2 Contributors, so the
/// dialog attributed the copyright to the wrong party in every language but English and German.
///
/// The upstream credit is not politeness: ScanMe is a GPL fork, and the licence's condition for
/// distributing it is that the original authors' notices are kept intact. This dialog and the LICENSE
/// files are where they are kept, and the licence line plus the source offer are what a recipient needs
/// in order to exercise the rights the GPL gives them.
///
/// Every label here is <c>NoWrap</c> -- the layout engine does not wrap a label without an explicit
/// <c>DynamicWrap</c> and <c>MaxWidth</c>, so the lines are broken by hand and each one has to stay
/// short enough to survive the German translation.
/// </remarks>
public class AboutForm : EtoDialogBase
{
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
            L.Column(
                new ImageView { Image = Icons.scanme_128.ToEtoImage() },
                C.Filler()
            ).Padding(right: 12),
            L.Column(
                C.Subtitle(AppBranding.Name),
                C.NoWrap(string.Format(MiscResources.Version, AssemblyHelper.Version)),
                C.UrlLink(AppBranding.HomepageUrl),
                C.TextSpace(),
                C.NoWrap(AppBranding.Copyright),
                C.Secondary(UiStrings.AboutLicense),
                C.Secondary(UiStrings.AboutSourceAvailable),
                C.TextSpace(),
                C.Secondary(string.Format(
                    UiStrings.AboutBasedOnFormat, AppBranding.UpstreamName, AppBranding.UpstreamCopyright)),
                C.UrlLink(AppBranding.UpstreamUrl),
                C.Secondary(string.Format(UiStrings.AboutIconsFormat, AppBranding.IconsName)),
                C.TextSpace(),
                L.Row(
                    L.Column(
                        C.Filler(),
                        Config.AppLocked.Has(c => c.EnableDebugLogging)
                            ? C.None()
                            : _enableDebugLogging
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
