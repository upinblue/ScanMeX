using System.Globalization;
using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Sap;

namespace NAPS2.EtoForms.Ui;

internal class SettingsForm : EtoDialogBase
{
    private readonly DesktopFormProvider _desktopFormProvider;
    private readonly EnumDropDownWidget<Theme> _theme = new(scale: false);
    private readonly CheckBox _scanChangesDefaultProfile = C.CheckBox(UiStrings.ScanChangesDefaultProfile);
    private readonly CheckBox _showProfilesToolbar = C.CheckBox(UiStrings.ShowProfilesToolbar);
    private readonly CheckBox _showPageNumbers = C.CheckBox(UiStrings.ShowPageNumbers);
    private readonly EnumDropDownWidget<ScanButtonDefaultAction> _scanButtonDefaultAction = new(scale: false);
    private readonly EnumDropDownWidget<SaveButtonDefaultAction> _saveButtonDefaultAction = new(scale: false);
    private readonly CheckBox _clearAfterSaving = C.CheckBox(UiStrings.ClearAfterSaving);
    private readonly CheckBox _keepSession = C.CheckBox(UiStrings.KeepSession);
    private readonly CheckBox _singleInstance = C.CheckBox(UiStrings.SingleInstanceDesc);
    private readonly ActionCommand _pdfSettingsCommand;
    private readonly ActionCommand _imageSettingsCommand;
    private readonly ActionCommand _emailSettingsCommand;
    private readonly ActionCommand _keyboardShortcutsCommand;
    private readonly Button _sapConnection = new() { Text = UiStrings.SapConnectionTitle };
    private readonly Button _restoreDefaults = new() { Text = UiStrings.RestoreDefaults };
    // Language and About used to be toolbar buttons. Windows apps keep both on their settings page,
    // and the toolbar is for what the operator does with the pages in front of them.
    private readonly DropDown _language = C.DropDown(scale: false);
    private readonly LinkButton _about;
    private readonly List<(string code, string name)> _cultures;
    private readonly bool _showLanguage;
    private readonly bool _showAbout;

    public SettingsForm(Naps2Config config, DesktopSubFormController desktopSubFormController,
        DesktopFormProvider desktopFormProvider, IIconProvider iconProvider, CultureHelper cultureHelper)
        : base(config)
    {
        Title = UiStrings.SettingsFormTitle;
        IconName = "cog_small";

        _desktopFormProvider = desktopFormProvider;
        var hiddenButtons = config.Get(c => c.HiddenButtons);
        _showLanguage = !hiddenButtons.HasFlag(ToolbarButtons.Language);
        _showAbout = !hiddenButtons.HasFlag(ToolbarButtons.About);
        _cultures = cultureHelper.GetAvailableCultures().ToList();
        foreach (var (_, name) in _cultures)
        {
            _language.Items.Add(name);
        }
        _language.SelectedIndex = IndexOfCurrentCulture();
        _language.Enabled = !config.AppLocked.Has(c => c.Culture);
        _about = C.Link(string.Format(UiStrings.AboutAppFormat, AppBranding.Name),
            desktopSubFormController.ShowAboutForm);
        UpdateValues(Config);
        _restoreDefaults.Click += RestoreDefaults_Click;

        _pdfSettingsCommand = new ActionCommand(desktopSubFormController.ShowPdfSettingsForm)
        {
            Text = UiStrings.PdfSettings,
            Image = iconProvider.GetIcon("file_extension_pdf_small")
        };
        _imageSettingsCommand = new ActionCommand(desktopSubFormController.ShowImageSettingsForm)
        {
            Text = UiStrings.ImageSettings,
            Image = iconProvider.GetIcon("picture_small")
        };
        _emailSettingsCommand = new ActionCommand(desktopSubFormController.ShowEmailSettingsForm)
        {
            Text = UiStrings.EmailSettings,
            Image = iconProvider.GetIcon("email_small")
        };
        _keyboardShortcutsCommand = new ActionCommand(() => FormFactory.Create<KeyboardShortcutsForm>().ShowModal())
        {
            Text = UiStrings.KeyboardShortcuts,
            Image = iconProvider.GetIcon("keyboard_small")
        };
        _sapConnection.Click += (_, _) => FormFactory.Create<SapConnectionForm>().ShowModal(this);
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Size(60, 0);
        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            L.GroupBox(
                UiStrings.Interface,
                L.Column(
                    _showLanguage
                        ? L.Row(
                            C.Label(UiStrings.LanguageLabel).AlignCenter().Padding(right: 20),
                            _language.MinWidth(160)
                        ).Aligned()
                        : C.None(),
                    PlatformCompat.System.SupportsTheme
                        ? L.Row(
                            C.Label(UiStrings.ThemeLabel).AlignCenter().Padding(right: 20),
                            _theme.AsControl().MinWidth(100)
                        ).Aligned()
                        : C.None(),
                    PlatformCompat.System.SupportsShowPageNumbers ? _showPageNumbers : C.None(),
                    PlatformCompat.System.SupportsProfilesToolbar ? _showProfilesToolbar : C.None(),
                    _scanChangesDefaultProfile,
                    PlatformCompat.System.SupportsButtonActions
                        ? L.Row(
                            C.Label(UiStrings.ScanButtonDefaultAction).AlignCenter().Padding(right: 20),
                            _scanButtonDefaultAction
                        ).Aligned()
                        : C.None(),
                    PlatformCompat.System.SupportsButtonActions
                        ? L.Row(
                            C.Label(UiStrings.SaveButtonDefaultAction).AlignCenter().Padding(right: 20),
                            _saveButtonDefaultAction
                        ).Aligned()
                        : C.None(),
                    PlatformCompat.System.SupportsKeyboardShortcuts
                        ? C.Button(_keyboardShortcutsCommand, ButtonImagePosition.Left).AlignLeading()
                        : C.None(),
                    _sapConnection.AlignLeading()
                )
            ),
            L.GroupBox(
                UiStrings.Application,
                L.Column(
                    _clearAfterSaving,
                    _keepSession,
                    PlatformCompat.System.SupportsSingleInstance
                        ? _singleInstance
                        : C.None()
                )
            ),
            // TODO: Probably only show these after we start adding tabs
            // L.Row(
            //     C.Button(_pdfSettingsCommand, ButtonImagePosition.Left),
            //     C.Button(_imageSettingsCommand, ButtonImagePosition.Left),
            //     C.Button(_emailSettingsCommand, ButtonImagePosition.Left)),
            C.Filler(),
            _showAbout ? _about.AlignLeading() : C.None(),
            L.Row(
                _restoreDefaults.MinWidth(140),
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, Save),
                    C.CancelButton(this))
            )
        );
    }

    private void UpdateValues(Naps2Config config)
    {
        void UpdateCheckbox(CheckBox checkBox, Expression<Func<CommonConfig, bool>> accessor)
        {
            checkBox.Checked = config.Get(accessor);
            checkBox.Enabled = !config.AppLocked.Has(accessor);
        }

        _theme.SelectedItem = config.Get(c => c.Theme);
        _theme.Enabled = !config.AppLocked.Has(c => c.Theme);
        UpdateCheckbox(_scanChangesDefaultProfile, c => c.ScanChangesDefaultProfile);
        UpdateCheckbox(_showProfilesToolbar, c => c.ShowProfilesToolbar);
        UpdateCheckbox(_showPageNumbers, c => c.ShowPageNumbers);
        _scanButtonDefaultAction.SelectedItem = config.Get(c => c.ScanButtonDefaultAction);
        _scanButtonDefaultAction.Enabled = !config.AppLocked.Has(c => c.ScanButtonDefaultAction);
        _saveButtonDefaultAction.SelectedItem = config.Get(c => c.SaveButtonDefaultAction);
        _saveButtonDefaultAction.Enabled = !config.AppLocked.Has(c => c.SaveButtonDefaultAction);
        UpdateCheckbox(_clearAfterSaving, c => c.DeleteAfterSaving);
        UpdateCheckbox(_keepSession, c => c.KeepSession);
        UpdateCheckbox(_singleInstance, c => c.SingleInstance);
    }

    private void Save()
    {
        var transact = Config.User.BeginTransaction();
        bool SetIfChanged<T>(Expression<Func<CommonConfig, T>> accessor, T value)
        {
            var oldValue = Config.Get(accessor);
            if (!Equals(value, oldValue))
            {
                transact.Set(accessor, value);
                return true;
            }
            return false;
        }
        bool themeChanged = SetIfChanged(c => c.Theme, _theme.SelectedItem);
        SetIfChanged(c => c.ScanChangesDefaultProfile, _scanChangesDefaultProfile.IsChecked());
        SetIfChanged(c => c.ShowProfilesToolbar, _showProfilesToolbar.IsChecked());
        SetIfChanged(c => c.ShowPageNumbers, _showPageNumbers.IsChecked());
        SetIfChanged(c => c.ScanButtonDefaultAction, _scanButtonDefaultAction.SelectedItem);
        SetIfChanged(c => c.SaveButtonDefaultAction, _saveButtonDefaultAction.SelectedItem);
        SetIfChanged(c => c.DeleteAfterSaving, _clearAfterSaving.IsChecked());
        SetIfChanged(c => c.KeepSession, _keepSession.IsChecked());
        SetIfChanged(c => c.SingleInstance, _singleInstance.IsChecked());
        transact.Commit();

        var desktop = _desktopFormProvider.DesktopForm;
        desktop.Invalidate();
        desktop.PlaceProfilesToolbar();
        if (themeChanged)
        {
            EtoPlatform.Current.ColorScheme.UserThemeChanged();
        }

        var index = _language.SelectedIndex;
        if (_showLanguage && _language.Enabled && index >= 0 && index != IndexOfCurrentCulture())
        {
            var cultureId = _cultures[index].code;
            // After this dialog has closed: switching rebuilds the main window, which takes its open
            // modal children with it.
            Application.Instance.AsyncInvoke(() => desktop.ChangeCulture(cultureId));
        }
    }

    /// <summary>
    /// The language the UI is in now: the one in the config, or -- when none was ever picked -- the
    /// one Windows chose, matched by name and then by language alone (de-AT runs the German strings).
    /// </summary>
    private int IndexOfCurrentCulture()
    {
        var current = Config.Get(c => c.Culture);
        if (string.IsNullOrWhiteSpace(current))
        {
            current = CultureInfo.CurrentUICulture.Name;
        }
        var exact = _cultures.FindIndex(x => string.Equals(x.code, current, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact;
        var language = current.Split('-')[0];
        var byLanguage = _cultures.FindIndex(x => string.Equals(x.code, language, StringComparison.OrdinalIgnoreCase));
        return byLanguage >= 0 ? byLanguage : _cultures.FindIndex(x => x.code == "en");
    }

    private void RestoreDefaults_Click(object? sender, EventArgs e)
    {
        UpdateValues(Config.DefaultsOnly);
    }
}