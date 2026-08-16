using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Sap;
using System.Threading;

namespace NAPS2.EtoForms.Ui;

internal class SapConnectionForm : EtoDialogBase
{
    private readonly TextBox _host = new();
    private readonly TextBox _serviceName = new();
    private readonly TextBox _client = new();
    private readonly DropDownWidget<string> _language = new(scale: false);
    private readonly TextBox _user = new();
    private readonly PasswordBox _password = new();
    private readonly CheckBox _ignoreCertificateErrors = new() { Text = UiStrings.SapIgnoreSslCertificateCheck };
    private readonly Label _certificateWarning = new()
    {
        Text = UiStrings.SapCertificateWarning,
        TextColor = Colors.Red
    };
    private readonly LayoutVisibility _certificateWarningVis = new(false);
    private readonly Button _testConnection = new() { Text = UiStrings.SapTestConnection };
    private readonly Label _testResult = new();

    public SapConnectionForm(Naps2Config config) : base(config)
    {
        Title = UiStrings.SapConnectionTitle;
        IconName = "cog_small";
        _language.Items = new[] { "DE", "EN", "FR", "IT", "ES" };
        _ignoreCertificateErrors.CheckedChanged += (_, _) =>
            _certificateWarningVis.IsVisible = _ignoreCertificateErrors.IsChecked();
        _testConnection.Click += TestConnection_Click;
        LoadValues(config.Get(c => c.SapConnection));
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Size(120, 0);
        FormStateController.FixedHeightLayout = true;

        LayoutController.Content = L.Column(
            L.GroupBox(UiStrings.SapConnectionTitle, L.Column(
                C.Label(UiStrings.SapHostLabel), _host,
                C.Label(UiStrings.SapServiceNameLabel), _serviceName,
                C.Label(UiStrings.SapClientLabel), _client,
                C.Label(UiStrings.SapLanguageLabel), _language,
                C.Label(UiStrings.SapUserLabel), _user,
                C.Label(UiStrings.SapPasswordLabel), _password,
                _ignoreCertificateErrors,
                _certificateWarning.Visible(_certificateWarningVis),
                L.Row(_testConnection, _testResult)
            )),
            C.Filler(),
            L.Row(C.Filler(), L.OkCancel(C.OkButton(this, Save), C.CancelButton(this)))
        );
    }

    private void LoadValues(SapConnectionConfig config)
    {
        _host.Text = config.Host ?? "";
        _serviceName.Text = string.IsNullOrWhiteSpace(config.ServiceName) ? "ZARCHIVE_UPLOAD_SRV" : config.ServiceName;
        _client.Text = config.Client ?? "";
        _language.SelectedItem = string.IsNullOrWhiteSpace(config.Language) ? "DE" : config.Language;
        _user.Text = config.User ?? "";
        _password.Text = "";
        _ignoreCertificateErrors.Checked = config.IgnoreCertificateErrors;
        _certificateWarningVis.IsVisible = config.IgnoreCertificateErrors;
    }

    private SapConnectionConfig BuildConfig()
    {
        var current = Config.Get(c => c.SapConnection);
        var result = new SapConnectionConfig
        {
            Host = _host.Text.Trim().TrimEnd('/'),
            ServiceName = _serviceName.Text.Trim(),
            Client = _client.Text.Trim(),
            Language = _language.SelectedItem ?? "DE",
            User = _user.Text.Trim(),
            EncryptedPassword = current.EncryptedPassword,
            IgnoreCertificateErrors = _ignoreCertificateErrors.IsChecked()
        };
        if (!string.IsNullOrEmpty(_password.Text))
        {
            SapCredentialStore.WritePassword(result, _password.Text);
        }
        return result;
    }

    private bool ValidateConfig(SapConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Host) || !config.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, UiStrings.SapValidationHostRequired, UiStrings.SapConnectionTitle,
                MessageBoxButtons.OK, MessageBoxType.Error);
            _host.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(config.ServiceName))
        {
            MessageBox.Show(this, UiStrings.SapValidationServiceNameRequired, UiStrings.SapConnectionTitle,
                MessageBoxButtons.OK, MessageBoxType.Error);
            _serviceName.Focus();
            return false;
        }
        if (config.Client?.Length != 3 || !config.Client.All(char.IsDigit))
        {
            MessageBox.Show(this, UiStrings.SapValidationClientRequired, UiStrings.SapConnectionTitle,
                MessageBoxButtons.OK, MessageBoxType.Error);
            _client.Focus();
            return false;
        }
        return true;
    }

    private async void TestConnection_Click(object? sender, EventArgs e)
    {
        var config = BuildConfig();
        if (!ValidateConfig(config))
        {
            return;
        }
        _testResult.Text = UiStrings.Ellipsis;
        using var uploader = new HttpSapArchiveUploader(config);
        var result = await uploader.TestConnectionAsync(CancellationToken.None);
        _testResult.TextColor = result.Success ? Colors.Green : Colors.Red;
        _testResult.Text = result.Success
            ? string.Format(UiStrings.SapCsrfTokenReceived, Shorten(result.CsrfToken))
            : result.ErrorMessage ?? UiStrings.SapConnectionTestFailed;
    }

    private void Save()
    {
        var config = BuildConfig();
        if (!ValidateConfig(config))
        {
            return;
        }
        Config.User.Set(c => c.SapConnection, config);
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        return value.Length <= 12 ? value : value.Substring(0, 6) + "..." + value.Substring(value.Length - 4);
    }
}
