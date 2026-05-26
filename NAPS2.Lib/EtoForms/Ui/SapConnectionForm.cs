using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Sap;

namespace NAPS2.EtoForms.Ui;

internal class SapConnectionForm : EtoDialogBase
{
    private readonly EnumDropDownWidget<ConnectionMode> _connectionMode = new(scale: false);
    private readonly TextBox _systemId = new();
    private readonly TextBox _appServerHost = new();
    private readonly TextBox _systemNumber = new();
    private readonly TextBox _client = new();
    private readonly TextBox _language = new();
    private readonly TextBox _user = new();
    private readonly PasswordBox _password = new();
    private readonly TextBox _contentServerBaseUrl = new();
    private readonly CheckBox _useHttps = new() { Text = "HTTPS" };
    private readonly CheckBox _ignoreCertificateErrors = new() { Text = "Ignore certificate errors" };
    private readonly EnumDropDownWidget<ConnectionInsertMode> _connectionInsertMode = new(scale: false);
    private readonly TextBox _customRfcName = new();
    private readonly Button _testConnection = new() { Text = SapUi.TestConnection };

    public SapConnectionForm(Naps2Config config) : base(config)
    {
        Title = SapUi.SapConnection;
        IconName = "cog_small";
        _testConnection.Click += TestConnection_Click;
        LoadValues(config.Get(c => c.SapConnection));
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Eto.Drawing.Size(120, 0);
        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            L.GroupBox(SapUi.SapConnection, L.Column(
                C.Label("Connection mode"), _connectionMode,
                C.Label("System ID"), _systemId,
                C.Label("Application server host"), _appServerHost,
                C.Label("System number"), _systemNumber,
                C.Label("Client"), _client,
                C.Label("Language"), _language,
                C.Label("User"), _user,
                C.Label("Password (leave blank to keep existing)"), _password,
                C.Label("Content Server Base URL"), _contentServerBaseUrl,
                _useHttps,
                _ignoreCertificateErrors,
                C.Label("Connection insert mode"), _connectionInsertMode,
                C.Label("Custom RFC name"), _customRfcName,
                _testConnection
            )),
            C.Filler(),
            L.Row(C.Filler(), L.OkCancel(C.OkButton(this, Save), C.CancelButton(this)))
        );
    }

    private void LoadValues(SapConnectionConfig config)
    {
        _connectionMode.SelectedItem = config.ConnectionMode;
        _systemId.Text = config.SystemId ?? "";
        _appServerHost.Text = config.AppServerHost ?? "";
        _systemNumber.Text = config.SystemNumber ?? "";
        _client.Text = config.Client ?? "";
        _language.Text = config.Language ?? "";
        _user.Text = config.User ?? "";
        _contentServerBaseUrl.Text = config.ContentServerBaseUrl ?? "";
        _useHttps.Checked = config.UseHttps;
        _ignoreCertificateErrors.Checked = config.IgnoreCertificateErrors;
        _connectionInsertMode.SelectedItem = config.ConnectionInsertMode;
        _customRfcName.Text = config.CustomRfcName ?? "";
    }

    private SapConnectionConfig BuildConfig()
    {
        var current = Config.Get(c => c.SapConnection);
        var result = new SapConnectionConfig
        {
            ConnectionMode = _connectionMode.SelectedItem,
            SystemId = _systemId.Text.Trim(),
            AppServerHost = _appServerHost.Text.Trim(),
            SystemNumber = _systemNumber.Text.Trim(),
            Client = _client.Text.Trim(),
            Language = _language.Text.Trim(),
            User = _user.Text.Trim(),
            EncryptedPassword = current.EncryptedPassword,
            ContentServerBaseUrl = _contentServerBaseUrl.Text.Trim(),
            UseHttps = _useHttps.IsChecked(),
            IgnoreCertificateErrors = _ignoreCertificateErrors.IsChecked(),
            ConnectionInsertMode = _connectionInsertMode.SelectedItem,
            CustomRfcName = _customRfcName.Text.Trim()
        };
        if (!string.IsNullOrEmpty(_password.Text))
        {
            SapCredentialStore.WritePassword(result, _password.Text);
        }
        return result;
    }

    private async void TestConnection_Click(object? sender, EventArgs e)
    {
        var result = await SapArchiveDiagnostics.TestConnectionAsync(BuildConfig());
        MessageBox.Show(this,
            result.Success ? SapUi.ConnectionOk : string.Format(SapUi.ConnectionFailed, result.ErrorMessage ?? result.ErrorCode),
            SapUi.SapConnection,
            MessageBoxButtons.OK,
            result.Success ? MessageBoxType.Information : MessageBoxType.Error);
    }

    private void Save()
    {
        Config.User.Set(c => c.SapConnection, BuildConfig());
    }
}
