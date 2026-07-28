using System.Globalization;
using System.Threading;
using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Scan;
using NAPS2.Scan.Internal;
using NAPS2.Sap;

namespace NAPS2.EtoForms.Ui;

public class EditProfileForm : EtoDialogBase
{
    private readonly ErrorOutput _errorOutput;
    private readonly ProfileNameTracker _profileNameTracker;
    private readonly DeviceCapsCache _deviceCapsCache;

    private readonly TextBox _displayName = new();
    private readonly DeviceSelectorWidget _deviceSelectorWidget;
    private readonly RadioButton _predefinedSettings;
    private readonly RadioButton _nativeUi;
    private readonly LayoutVisibility _nativeUiVis = new(true);
    private readonly EnumDropDownWidget<ScanSource> _paperSource = new();
    private readonly PageSizeDropDownWidget _pageSize;
    private readonly ResolutionDropDownWidget _resolution;
    private readonly EnumDropDownWidget<ScanBitDepth> _bitDepth = new();
    private readonly EnumDropDownWidget<ScanHorizontalAlign> _horAlign = new();
    private readonly EnumDropDownWidget<ScanScale> _scale = new();
    private readonly CheckBox _enableAutoSave = new() { Text = UiStrings.EnableAutoSave };
    private readonly LinkButton _autoSaveSettings = C.Link(UiStrings.AutoSaveSettings);
    private readonly Button _advanced = new() { Text = UiStrings.Advanced };
    private readonly SliderWithTextBox _brightnessSlider = new();
    private readonly SliderWithTextBox _contrastSlider = new();

    // SharePoint upload controls
    private readonly CheckBox _enableSharePointUpload = new() { Text = UiStrings.EnableSharePointUpload };
    private readonly TextBox _sharePointSiteUrl = new();
    private readonly TextBox _sharePointLibraryPath = new();
    private readonly TextBox _sharePointFolderPath = new();
    private readonly TextBox _azureAdTenantId = new();
    private readonly TextBox _azureAdClientId = new();
    private readonly PasswordBox _azureAdClientSecret = new();

    // SAP ArchiveLink controls
    private readonly CheckBox _enableSapArchiveUpload = new() { Text = SapUi.EnableUpload };
    private readonly TextBox _sapHost = new();
    private readonly TextBox _sapServiceName = new();
    private readonly TextBox _sapClient = new();
    private readonly TextBox _sapLanguage = new();
    private readonly TextBox _sapUser = new();
    private readonly PasswordBox _sapPassword = new();
    private readonly CheckBox _sapIgnoreSsl = new() { Text = "SSL-Zertifikatsprüfung deaktivieren (nur Testumgebung!)" };
    private readonly DropDownWidget<SapObjectTypeCatalogEntry> _sapObjectType = new();
    private readonly TextBox _sapArchiveId = new();
    private readonly TextBox _sapDocumentType = new();
    private readonly RadioButton _sapPromptObjectKey = new() { Text = SapUi.PromptEachScan };
    private readonly RadioButton _sapBarcodeObjectKey;
    private readonly RadioButton _sapFilenameObjectKey;
    private readonly RadioButton _sapFixedObjectKey;
    private readonly TextBox _sapBarcodeRegex = new();
    private readonly TextBox _sapFilenameRegex = new();
    private readonly TextBox _sapFixedObjectKeyValue = new();
    private readonly TextBox _sapDescriptionTemplate = new() { PlaceholderText = UiStrings.SapObjectIdPlaceholder };
    private readonly Button _sapTestConnection = new() { Text = UiStrings.SapTestUpload };

    private ScanProfile _scanProfile = null!;
    private bool _isDefault;
    private bool _result;
    private bool _suppressChangeEvent;
    private CancellationTokenSource? _updateCapsCts;

    public EditProfileForm(Naps2Config config, IScanPerformer scanPerformer, ErrorOutput errorOutput,
        ProfileNameTracker profileNameTracker, DeviceCapsCache deviceCapsCache,
        IIconProvider iconProvider) : base(config)
    {
        Title = UiStrings.EditProfileFormTitle;
        IconName = "blueprints_small";

        _errorOutput = errorOutput;
        _profileNameTracker = profileNameTracker;
        _deviceCapsCache = deviceCapsCache;
        _deviceSelectorWidget = new(scanPerformer, deviceCapsCache, iconProvider, this)
        {
            ProfileFunc = GetUpdatedScanProfile,
            AllowAlwaysAsk = true
        };
        _pageSize = new(this);
        _resolution = new(this);
        _deviceSelectorWidget.DeviceChanged += DeviceChanged;

        _predefinedSettings = new RadioButton { Text = UiStrings.UsePredefinedSettings };
        _nativeUi = new RadioButton(_predefinedSettings) { Text = UiStrings.UseNativeUi };
        _paperSource.SelectedItemChanged += PaperSource_SelectedItemChanged;
        _predefinedSettings.CheckedChanged += PredefinedSettings_CheckedChanged;
        _nativeUi.CheckedChanged += NativeUi_CheckedChanged;

        _enableAutoSave.CheckedChanged += EnableAutoSave_CheckedChanged;
        _autoSaveSettings.Click += AutoSaveSettings_LinkClicked;
        _advanced.Click += Advanced_Click;

        _enableSharePointUpload.CheckedChanged += EnableSharePointUpload_CheckedChanged;

        _sapBarcodeObjectKey = new RadioButton(_sapPromptObjectKey) { Text = SapUi.FromBarcode };
        _sapFilenameObjectKey = new RadioButton(_sapPromptObjectKey) { Text = SapUi.FromFilename };
        _sapFixedObjectKey = new RadioButton(_sapPromptObjectKey) { Text = SapUi.FixedValue };
        _sapObjectType.Format = x => $"{x.Key} - {x.DisplayName}";
        _sapObjectType.Items = SapObjectTypeCatalog.CommonTypes;
        _sapObjectType.SelectedItemChanged += (_, _) => UpdateSapObjectTypeTooltip();
        _enableSapArchiveUpload.CheckedChanged += (_, _) => UpdateSapControlsEnabled();
        _sapPromptObjectKey.CheckedChanged += (_, _) => UpdateSapControlsEnabled();
        _sapBarcodeObjectKey.CheckedChanged += (_, _) => UpdateSapControlsEnabled();
        _sapFilenameObjectKey.CheckedChanged += (_, _) => UpdateSapControlsEnabled();
        _sapFixedObjectKey.CheckedChanged += (_, _) => UpdateSapControlsEnabled();
        _sapTestConnection.Click += SapTestConnection_Click;
    }

    public void SetDevice(ScanDevice device)
    {
        _deviceSelectorWidget.Choice = DeviceChoice.ForDevice(device);
    }

    private void DeviceChanged(object? sender, DeviceChangedEventArgs e)
    {
        if (e.NewChoice.Device != null && (string.IsNullOrEmpty(_displayName.Text) ||
                                           e.PreviousChoice.Device?.Name == _displayName.Text))
        {
            _displayName.Text = e.NewChoice.Device.Name;
        }
        DeviceDriver = e.NewChoice.Driver;
        IconUri = e.NewChoice.Device?.IconUri;

        UpdateCaps();
        UpdateEnabledControls();
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Size(60, 0);
        FormStateController.FixedHeightLayout = true;

        var scannerSettings = L.GroupBox(
            UiStrings.ProfileScannerSection,
            L.Column(
                C.Label(UiStrings.DisplayNameLabel),
                _displayName,
                C.Spacer(),
                _deviceSelectorWidget,
                C.Spacer(),
                PlatformCompat.System.IsWiaDriverSupported || PlatformCompat.System.IsTwainDriverSupported
                    ? L.Row(
                        _predefinedSettings,
                        _nativeUi
                    ).Visible(_nativeUiVis)
                    : C.None(),
                C.Spacer(),
                L.Row(
                    L.Column(
                        C.Label(UiStrings.PaperSourceLabel),
                        _paperSource,
                        C.Label(UiStrings.PageSizeLabel),
                        _pageSize,
                        C.Label(UiStrings.ResolutionLabel),
                        _resolution,
                        C.Label(UiStrings.BrightnessLabel),
                        _brightnessSlider
                    ).Scale(),
                    L.Column(
                        C.Label(UiStrings.BitDepthLabel),
                        _bitDepth,
                        C.Label(UiStrings.HorizontalAlignLabel),
                        _horAlign,
                        C.Label(UiStrings.ScaleLabel),
                        _scale,
                        C.Label(UiStrings.ContrastLabel),
                        _contrastSlider
                    ).Scale()
                )
            )
        );

        var autoSaveUploadSettings = L.GroupBox(
            UiStrings.ProfileAutoSaveUploadsSection,
            L.Column(
                L.Row(
                    _enableAutoSave,
                    _autoSaveSettings
                ),
                C.Label(UiStrings.UploadRequiresAutoSaveInfo)
            )
        );

        var sharePointSettings = L.GroupBox(
            UiStrings.SharePointUpload,
            L.Column(
                _enableSharePointUpload,
                C.Label(UiStrings.SharePointSiteUrlLabel),
                _sharePointSiteUrl,
                C.Label(UiStrings.SharePointLibraryPathLabel),
                _sharePointLibraryPath,
                C.Label(UiStrings.SharePointFolderPathLabel),
                _sharePointFolderPath,
                C.Label(UiStrings.AzureAdTenantIdLabel),
                _azureAdTenantId,
                C.Label(UiStrings.AzureAdClientIdLabel),
                _azureAdClientId,
                C.Label(UiStrings.AzureAdClientSecretLabel),
                _azureAdClientSecret
            )
        );

        var sapSettings = L.GroupBox(
            SapUi.ArchiveLink,
            L.Column(
                _enableSapArchiveUpload,
                L.Row(
                    L.Column(
                        C.Label(UiStrings.SapHostLabel),
                        _sapHost,
                        C.Label(UiStrings.SapServiceNameLabel),
                        _sapServiceName,
                        C.Label(UiStrings.SapClientLabel),
                        _sapClient,
                        C.Label(UiStrings.SapLanguageLabel),
                        _sapLanguage
                    ).Scale(),
                    L.Column(
                        C.Label(UiStrings.SapUserLabel),
                        _sapUser,
                        C.Label(UiStrings.SapPasswordLabel),
                        _sapPassword,
                        _sapIgnoreSsl
                    ).Scale()
                ),
                C.Spacer(),
                L.Row(
                    L.Column(
                        C.Label(SapUi.ArchiveId),
                        _sapArchiveId,
                        C.Label(UiStrings.SapArObjectLabel),
                        _sapObjectType,
                        C.Label(UiStrings.SapObjectLabel),
                        _sapDocumentType
                    ).Scale(),
                    L.Column(
                        C.Label(SapUi.ObjectKeySource),
                        _sapPromptObjectKey,
                        _sapBarcodeObjectKey,
                        C.Label(UiStrings.Code39RegexOptionalLabel),
                        _sapBarcodeRegex,
                        _sapFilenameObjectKey,
                        C.Label(SapUi.Regex),
                        _sapFilenameRegex,
                        _sapFixedObjectKey,
                        C.Label(SapUi.FixedValue),
                        _sapFixedObjectKeyValue,
                        C.Label(UiStrings.SapObjectIdLabel),
                        _sapDescriptionTemplate
                    ).Scale()
                ),
                _sapTestConnection
            )
        );

        var scrollableContent = L.Column(
            scannerSettings,
            autoSaveUploadSettings,
            sharePointSettings,
            sapSettings
        );

        LayoutController.Content = L.Column(
            L.Scrollable(scrollableContent),
            L.Row(
                _advanced,
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, SaveSettings),
                    C.CancelButton(this))
            )
        );
    }

    public bool Result => _result;

    public ScanProfile ScanProfile
    {
        get => _scanProfile;
        set
        {
            _scanProfile = value.Clone();
            UpdateUiForScanProfile();
        }
    }

    public bool NewProfile { get; set; }

    private void UpdateUiForCaps()
    {
        _suppressChangeEvent = true;

        _paperSource.Items = ScanProfile.Caps?.PaperSources?.Values is [_, ..] paperSources
            ? paperSources
            : EnumDropDownWidget<ScanSource>.DefaultItems;

        var selectedSource = _paperSource.SelectedItem;
        var perSource = selectedSource switch
        {
            ScanSource.Glass => ScanProfile.Caps?.Glass,
            ScanSource.Feeder => ScanProfile.Caps?.Feeder,
            ScanSource.Duplex => ScanProfile.Caps?.Duplex,
            _ => null
        };

        var validResolutions = perSource?.Resolutions;
        _resolution.VisiblePresets = validResolutions is [_, ..]
            ? validResolutions
            : EnumDropDownWidget<ScanDpi>.DefaultItems.Select(x => x.ToIntDpi());

        var scanArea = perSource?.ScanArea;
        var sizeCaps = new PageSizeCaps { ScanArea = scanArea };

        var allPresets = EnumDropDownWidget<ScanPageSize>.DefaultItems.SkipLast(2).ToList();
        var conditionalPresets = new[] { ScanPageSize.A3, ScanPageSize.B4 };
        _pageSize.VisiblePresets = allPresets.Where(preset =>
            !conditionalPresets.Contains(preset) || sizeCaps.Fits(preset.PageDimensions()!.ToPageSize()));

        _suppressChangeEvent = false;
    }

    private void UpdateCaps()
    {
        var cts = new CancellationTokenSource();
        _updateCapsCts?.Cancel();
        _updateCapsCts = cts;
        var updatedProfile = GetUpdatedScanProfile();
        var cachedCaps = _deviceCapsCache.GetCachedCaps(updatedProfile);
        if (cachedCaps != null)
        {
            ScanProfile.Caps = MapCaps(cachedCaps);
        }
        else
        {
            ScanProfile.Caps = null;
            if (updatedProfile.Device != null)
            {
                Task.Run(async () =>
                {
                    var caps = await _deviceCapsCache.QueryCaps(updatedProfile);
                    if (caps != null)
                    {
                        Invoker.Current.Invoke(() =>
                        {
                            if (!cts.IsCancellationRequested)
                            {
                                ScanProfile.Caps = MapCaps(caps);
                                UpdateUiForCaps();
                            }
                        });
                    }
                });
            }
        }
        UpdateUiForCaps();
    }

    private ScanProfileCaps MapCaps(ScanCaps? caps)
    {
        List<ScanSource>? paperSources = null;
        if (caps?.PaperSourceCaps is { } paperSourceCaps)
        {
            paperSources = new List<ScanSource>();
            if (paperSourceCaps.SupportsFlatbed) paperSources.Add(ScanSource.Glass);
            if (paperSourceCaps.SupportsFeeder) paperSources.Add(ScanSource.Feeder);
            if (paperSourceCaps.SupportsDuplex) paperSources.Add(ScanSource.Duplex);
        }

        return new ScanProfileCaps
        {
            PaperSources = new PaperSourceProfileCaps { Values = paperSources },
            FeederCheck = caps?.PaperSourceCaps?.CanCheckIfFeederHasPaper,
            Glass = new PerSourceProfileCaps
            {
                ScanArea = caps?.FlatbedCaps?.PageSizeCaps?.ScanArea,
                Resolutions = caps?.FlatbedCaps?.DpiCaps?.CommonValues?.ToList()
            },
            Feeder = new PerSourceProfileCaps
            {
                ScanArea = caps?.FeederCaps?.PageSizeCaps?.ScanArea,
                Resolutions = caps?.FeederCaps?.DpiCaps?.CommonValues?.ToList()
            },
            Duplex = new PerSourceProfileCaps
            {
                ScanArea = caps?.DuplexCaps?.PageSizeCaps?.ScanArea,
                Resolutions = caps?.DuplexCaps?.DpiCaps?.CommonValues?.ToList()
            }
        };
    }

    private Driver DeviceDriver { get; set; }

    private string? IconUri { get; set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        UpdateUiForCaps();
        UpdateEnabledControls();
    }

    private void UpdateUiForScanProfile()
    {
        // Don't trigger any onChange events
        _suppressChangeEvent = true;

        DeviceDriver = new ScanOptionsValidator().ValidateDriver(
            Enum.TryParse<Driver>(ScanProfile.DriverName, true, out var driver)
                ? driver
                : Driver.Default);
        IconUri = ScanProfile.Device?.IconUri;

        _displayName.Text = ScanProfile.DisplayName;
        if (_deviceSelectorWidget.Choice == DeviceChoice.None)
        {
            var device = ScanProfile.Device?.ToScanDevice(DeviceDriver);
            if (device != null)
            {
                _deviceSelectorWidget.Choice = DeviceChoice.ForDevice(device);
            }
            else if (!NewProfile)
            {
                _deviceSelectorWidget.Choice = DeviceChoice.ForAlwaysAsk(DeviceDriver);
            }
        }
        _isDefault = ScanProfile.IsDefault;

        if (ScanProfile.PageSize == ScanPageSize.Custom && ScanProfile.CustomPageSize != null)
        {
            _pageSize.SetCustom(ScanProfile.CustomPageSizeName, ScanProfile.CustomPageSize);
        }
        else
        {
            _pageSize.SetPreset(ScanProfile.PageSize);
        }

        _paperSource.SelectedItem = ScanProfile.PaperSource;
        _bitDepth.SelectedItem = ScanProfile.BitDepth;
        _resolution.SetDpi(ScanProfile.Resolution.Dpi);
        _contrastSlider.IntValue = ScanProfile.Contrast;
        _brightnessSlider.IntValue = ScanProfile.Brightness;
        _scale.SelectedItem = ScanProfile.AfterScanScale;
        _horAlign.SelectedItem = ScanProfile.PageAlign;

        _enableAutoSave.Checked = ScanProfile.EnableAutoSave;

        _nativeUi.Checked = ScanProfile.UseNativeUI;
        _predefinedSettings.Checked = !ScanProfile.UseNativeUI;

        // SharePoint settings
        _enableSharePointUpload.Checked = ScanProfile.EnableSharePointUpload;
        _sharePointSiteUrl.Text = ScanProfile.SharePointUploadSettings.SiteUrl ?? "";
        _sharePointLibraryPath.Text = ScanProfile.SharePointUploadSettings.LibraryNameOrPath ?? "";
        _sharePointFolderPath.Text = ScanProfile.SharePointUploadSettings.FolderPath ?? "";
        _azureAdTenantId.Text = ScanProfile.SharePointUploadSettings.TenantId ?? "";
        _azureAdClientId.Text = ScanProfile.SharePointUploadSettings.ClientId ?? "";
        _azureAdClientSecret.Text = ScanProfile.SharePointUploadSettings.ClientSecret ?? "";

        var sap = ScanProfile.SapArchiveSettings ?? new SapArchiveProfileSettings();
        var sapConnection = sap.Connection ?? Config.Get(c => c.SapConnection);
        _enableSapArchiveUpload.Checked = sap.EnableUpload;
        _sapHost.Text = sapConnection.Host ?? "";
        _sapServiceName.Text = string.IsNullOrWhiteSpace(sapConnection.ServiceName) ? "ZARCHIVE_UPLOAD_SRV" : sapConnection.ServiceName;
        _sapClient.Text = sapConnection.Client ?? "";
        _sapLanguage.Text = string.IsNullOrWhiteSpace(sapConnection.Language) ? "DE" : sapConnection.Language;
        _sapUser.Text = sapConnection.User ?? "";
        _sapPassword.Text = "";
        _sapIgnoreSsl.Checked = sapConnection.IgnoreCertificateErrors;
        _sapArchiveId.Text = sap.ArchiveId ?? "";
        _sapDocumentType.Text = sap.SapObject ?? "";
        _sapDescriptionTemplate.Text = sap.ObjectId ?? "";
        _sapBarcodeRegex.Text = sap.BarcodeRegex ?? "";
        _sapFilenameRegex.Text = sap.BarcodeRegex ?? "";
        _sapFixedObjectKeyValue.Text = sap.FixedBarcode ?? "";
        var selectedType = SapObjectTypeCatalog.CommonTypes.FirstOrDefault(x => x.Key == sap.ArObject) ?? SapObjectTypeCatalog.CommonTypes.FirstOrDefault();
        if (selectedType != null)
        {
            _sapObjectType.SelectedItem = selectedType;
        }
        _sapPromptObjectKey.Checked = sap.BarcodeSource == BarcodeSource.PromptUser;
        _sapBarcodeObjectKey.Checked = sap.BarcodeSource == BarcodeSource.FromScannedBarcode;
        _sapFilenameObjectKey.Checked = sap.BarcodeSource == BarcodeSource.FromFilename;
        _sapFixedObjectKey.Checked = sap.BarcodeSource == BarcodeSource.Fixed;
        UpdateSapObjectTypeTooltip();

        UpdateSharePointControlsEnabled();
        UpdateSapControlsEnabled();

        // Start triggering onChange events again
        _suppressChangeEvent = false;
    }

    private bool SaveSettings()
    {
        if (_displayName.Text == "")
        {
            _errorOutput.DisplayError(MiscResources.NameMissing);
            return false;
        }
        if (_deviceSelectorWidget.Choice == DeviceChoice.None)
        {
            _errorOutput.DisplayError(MiscResources.NoDeviceSelected);
            return false;
        }

        // Basic validation for SharePoint settings when enabled
        if (_enableSharePointUpload.IsChecked())
        {
            string site = _sharePointSiteUrl.Text.Trim();
            if (!site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _errorOutput.DisplayError(UiStrings.SharePointSiteUrlHttpsRequired);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_azureAdTenantId.Text) ||
                string.IsNullOrWhiteSpace(_azureAdClientId.Text) ||
                string.IsNullOrWhiteSpace(_azureAdClientSecret.Text))
            {
                _errorOutput.DisplayError(UiStrings.SharePointCredentialsRequired);
                return false;
            }
        }

        var sapValidation = BuildSapArchiveSettings().Validate();
        if (sapValidation.Count > 0)
        {
            _errorOutput.DisplayError(string.Join(Environment.NewLine, sapValidation));
            return false;
        }

        _result = true;

        if (ScanProfile.IsLocked)
        {
            if (!ScanProfile.IsDeviceLocked)
            {
                ScanProfile.Device = ScanProfileDevice.FromScanDevice(_deviceSelectorWidget.Choice.Device);
            }
            return true;
        }
        if (ScanProfile.DisplayName != null)
        {
            _profileNameTracker.RenamingProfile(ScanProfile.DisplayName, _displayName.Text);
        }
        _scanProfile = GetUpdatedScanProfile();
        return true;
    }

    private ScanProfile GetUpdatedScanProfile()
    {
        var pageSize = _pageSize.SelectedItem!;
        return new ScanProfile
        {
            Version = ScanProfile.CURRENT_VERSION,

            Device = ScanProfileDevice.FromScanDevice(_deviceSelectorWidget.Choice.Device),
            Caps = ScanProfile.Caps,
            IsDefault = _isDefault,
            DriverName = DeviceDriver.ToString().ToLowerInvariant(),
            DisplayName = _displayName.Text,
            IconID = 0,
            MaxQuality = ScanProfile.MaxQuality,
            UseNativeUI = _nativeUi.Checked,

            AfterScanScale = _scale.SelectedItem,
            BitDepth = _bitDepth.SelectedItem,
            Brightness = _brightnessSlider.IntValue,
            Contrast = _contrastSlider.IntValue,
            PageAlign = _horAlign.SelectedItem,
            PageSize = pageSize.Type,
            CustomPageSizeName = pageSize.CustomName,
            CustomPageSize = pageSize.CustomDimens,
            Resolution = new ScanResolution { Dpi = _resolution.SelectedItem?.Dpi ?? 0 },
            PaperSource = _paperSource.SelectedItem,

            EnableAutoSave = _enableAutoSave.IsChecked(),
            AutoSaveSettings = BuildAutoSaveSettings(),
            SapArchiveSettings = BuildSapArchiveSettings(),
            Quality = ScanProfile.Quality,

            // Settings this dialog doesn't edit. They are only reachable from the auto save dialog or
            // the advanced dialog, and this method builds a brand new profile, so anything not copied
            // here is silently reset the next time a profile is opened and confirmed.
            DocumentWorkflow = ScanProfile.DocumentWorkflow,
            BarcodeRecognitionEnabled = ScanProfile.BarcodeRecognitionEnabled,
            RotateDegrees = ScanProfile.RotateDegrees,
            KeyValueOptions = ScanProfile.KeyValueOptions,
            UpgradedFrom = ScanProfile.UpgradedFrom,

            BrightnessContrastAfterScan = ScanProfile.BrightnessContrastAfterScan,
            AutoDeskew = ScanProfile.AutoDeskew,
            WiaOffsetWidth = ScanProfile.WiaOffsetWidth,
            WiaRetryOnFailure = ScanProfile.WiaRetryOnFailure,
            WiaDelayBetweenScans = ScanProfile.WiaDelayBetweenScans,
            WiaDelayBetweenScansSeconds = ScanProfile.WiaDelayBetweenScansSeconds,
            WiaVersion = ScanProfile.WiaVersion,
            ForcePageSize = ScanProfile.ForcePageSize,
            ForcePageSizeCrop = ScanProfile.ForcePageSizeCrop,
            FlipDuplexedPages = ScanProfile.FlipDuplexedPages,
            TwainImpl = ScanProfile.TwainImpl,
            TwainProgress = ScanProfile.TwainProgress,

            ExcludeBlankPages = ScanProfile.ExcludeBlankPages,
            BlankPageWhiteThreshold = ScanProfile.BlankPageWhiteThreshold,
            BlankPageCoverageThreshold = ScanProfile.BlankPageCoverageThreshold,

            EnableSharePointUpload = _enableSharePointUpload.IsChecked(),
            SharePointUploadSettings = new SharePointUploadSettings
            {
                SiteUrl = _sharePointSiteUrl.Text.Trim(),
                LibraryNameOrPath = _sharePointLibraryPath.Text.Trim(),
                FolderPath = _sharePointFolderPath.Text.Trim(),
                TenantId = _azureAdTenantId.Text.Trim(),
                ClientId = _azureAdClientId.Text.Trim(),
                ClientSecret = _azureAdClientSecret.Text
            }
        };
    }

    private void PredefinedSettings_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateEnabledControls();
    }

    private void NativeUi_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateEnabledControls();
    }

    private void UpdateEnabledControls()
    {
        if (!_suppressChangeEvent)
        {
            _suppressChangeEvent = true;

            bool canUseNativeUi = DeviceDriver is Driver.Wia or Driver.Twain;
            bool locked = ScanProfile.IsLocked;
            bool deviceLocked = ScanProfile.IsDeviceLocked;
            bool settingsEnabled = !locked && (_predefinedSettings.Checked || !canUseNativeUi);

            _displayName.Enabled = !locked;
            _deviceSelectorWidget.Enabled = !deviceLocked;
            _predefinedSettings.Enabled = _nativeUi.Enabled = !locked;
            _nativeUiVis.IsVisible = _deviceSelectorWidget.Choice.Device == null || canUseNativeUi;

            _paperSource.Enabled = settingsEnabled;
            _resolution.Enabled = settingsEnabled;
            _pageSize.Enabled = settingsEnabled;
            _bitDepth.Enabled = settingsEnabled;
            _horAlign.Enabled = settingsEnabled;
            _scale.Enabled = settingsEnabled;
            _brightnessSlider.Enabled = settingsEnabled;
            _contrastSlider.Enabled = settingsEnabled;

            _enableAutoSave.Enabled = !locked && !Config.Get(c => c.DisableAutoSave);
            _autoSaveSettings.Enabled = _enableAutoSave.IsChecked();
            _autoSaveSettings.Visible = !locked && !Config.Get(c => c.DisableAutoSave);

            _advanced.Enabled = !locked;

            // SharePoint controls
            _enableSharePointUpload.Enabled = !locked;
            UpdateSharePointControlsEnabled();

            // SAP controls
            _enableSapArchiveUpload.Enabled = !locked;
            UpdateSapControlsEnabled();

            _suppressChangeEvent = false;
        }
    }

    /// <summary>
    /// Keeps the auto save upload flags in step with the enable checkboxes shown next to the credentials.
    /// The two used to be set in separate dialogs and could contradict each other, so ticking "enable
    /// SharePoint upload" here had no effect unless the auto save dialog was opened as well.
    /// </summary>
    /// <summary>
    /// Mirrors the enable checkboxes onto the profile object shared with the auto save dialog.
    /// </summary>
    private void SyncUploadTargetsToProfile()
    {
        ScanProfile.EnableSharePointUpload = _enableSharePointUpload.IsChecked();
        ScanProfile.AutoSaveSettings = BuildAutoSaveSettings();
        if (ScanProfile.SapArchiveSettings != null)
        {
            ScanProfile.SapArchiveSettings.EnableUpload = _enableSapArchiveUpload.IsChecked();
        }
    }

    private AutoSaveSettings BuildAutoSaveSettings()
    {
        var settings = ScanProfile.AutoSaveSettings ?? new AutoSaveSettings();
        return settings with
        {
            UploadToSharePoint = _enableSharePointUpload.IsChecked(),
            UploadToSap = _enableSapArchiveUpload.IsChecked()
        };
    }

    private SapArchiveProfileSettings BuildSapArchiveSettings()
    {
        var currentConnection = ScanProfile.SapArchiveSettings?.Connection ?? Config.Get(c => c.SapConnection);
        var connection = new SapConnectionConfig
        {
            Host = _sapHost.Text.Trim().TrimEnd('/'),
            ServiceName = string.IsNullOrWhiteSpace(_sapServiceName.Text) ? "ZARCHIVE_UPLOAD_SRV" : _sapServiceName.Text.Trim(),
            Client = _sapClient.Text.Trim(),
            Language = string.IsNullOrWhiteSpace(_sapLanguage.Text) ? "DE" : _sapLanguage.Text.Trim(),
            User = _sapUser.Text.Trim(),
            EncryptedPassword = currentConnection.EncryptedPassword,
            IgnoreCertificateErrors = _sapIgnoreSsl.IsChecked()
        };
        if (!string.IsNullOrEmpty(_sapPassword.Text))
        {
            SapCredentialStore.WritePassword(connection, _sapPassword.Text);
        }

        return new SapArchiveProfileSettings
        {
            EnableUpload = _enableSapArchiveUpload.IsChecked(),
            Connection = connection,
            ArchiveId = _sapArchiveId.Text.Trim(),
            ArObject = _sapObjectType.SelectedItem?.Key,
            SapObject = _sapDocumentType.Text.Trim(),
            ObjectId = _sapDescriptionTemplate.Text.Trim(),
            BarcodeSource = _sapBarcodeObjectKey.Checked ? BarcodeSource.FromScannedBarcode
                : _sapFilenameObjectKey.Checked ? BarcodeSource.FromFilename
                : _sapFixedObjectKey.Checked ? BarcodeSource.Fixed
                : BarcodeSource.PromptUser,
            BarcodeRegex = _sapBarcodeObjectKey.Checked ? _sapBarcodeRegex.Text.Trim() : _sapFilenameRegex.Text.Trim(),
            FixedBarcode = _sapFixedObjectKeyValue.Text.Trim()
        };
    }

    private void UpdateSapObjectTypeTooltip()
    {
        _sapObjectType.AsControl().ToolTip = _sapObjectType.SelectedItem?.KeyFormatHint ?? "";
    }

    private void UpdateSapControlsEnabled()
    {
        if (_scanProfile == null)
        {
            return;
        }
        bool enabled = _enableSapArchiveUpload.IsChecked() && !_scanProfile.IsLocked;
        _enableSapArchiveUpload.Enabled = !_scanProfile.IsLocked;
        _sapHost.Enabled = enabled;
        _sapServiceName.Enabled = enabled;
        _sapClient.Enabled = enabled;
        _sapLanguage.Enabled = enabled;
        _sapUser.Enabled = enabled;
        _sapPassword.Enabled = enabled;
        _sapIgnoreSsl.Enabled = enabled;
        _sapObjectType.Enabled = enabled;
        _sapArchiveId.Enabled = enabled;
        _sapDocumentType.Enabled = enabled;
        _sapPromptObjectKey.Enabled = enabled;
        _sapBarcodeObjectKey.Enabled = enabled;
        _sapFilenameObjectKey.Enabled = enabled;
        _sapFixedObjectKey.Enabled = enabled;
        _sapBarcodeRegex.Enabled = enabled && _sapBarcodeObjectKey.Checked;
        _sapFilenameRegex.Enabled = enabled && _sapFilenameObjectKey.Checked;
        _sapFixedObjectKeyValue.Enabled = enabled && _sapFixedObjectKey.Checked;
        _sapDescriptionTemplate.Enabled = enabled;
        _sapTestConnection.Enabled = enabled;
    }

    private async void SapTestConnection_Click(object? sender, EventArgs e)
    {
        var settings = BuildSapArchiveSettings();
        var validation = settings.Validate();
        if (validation.Count > 0)
        {
            _errorOutput.DisplayError(string.Join(Environment.NewLine, validation));
            return;
        }

        var filePath = PromptForSapTestPdf();
        if (filePath == null)
        {
            return;
        }

        var fileName = Path.GetFileName(filePath);
        var barcode = ResolveSapTestBarcode(settings, filePath);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            _errorOutput.DisplayError(UiStrings.SapTestUploadNoObjectKey);
            return;
        }

        var request = new SapUploadRequest(
            settings.Connection ?? Config.Get(c => c.SapConnection),
            settings,
            barcode,
            string.IsNullOrWhiteSpace(settings.ObjectId) ? null : settings.ObjectId.Replace("{barcode}", barcode, StringComparison.OrdinalIgnoreCase),
            await File.ReadAllBytesAsync(filePath),
            fileName,
            "application/pdf");
        var result = await new HttpSapArchiveUploader(request.Connection).UploadAsync(request, CancellationToken.None);
        MessageBox.Show(this,
            result.Success
                ? $"SAP-Upload OK – DocId: {result.ArchivDocId}, Barcode: {barcode}"
                : $"SAP-Upload fehlgeschlagen – HTTP: {result.HttpStatusCode}, Code: {result.ErrorCode}, Message: {result.ErrorMessage}, TransactionId: {result.TransactionId}",
            "SAP ArchiveLink",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxType.Information : MessageBoxType.Error);
    }

    private string? PromptForSapTestPdf()
    {
        var ofd = new OpenFileDialog
        {
            MultiSelect = false,
            CheckFileExists = true,
            Title = "PDF für SAP-Testupload auswählen"
        };
        ofd.Filters.Add(new FileFilter("PDF (*.pdf)", ".pdf"));
        EtoPlatform.Current.ConfigureFileDialog(ofd);
        if (ofd.ShowDialog(this) != DialogResult.Ok)
        {
            return null;
        }

        var filePath = ofd.Filenames.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _errorOutput.DisplayError("Keine PDF-Datei für den SAP-Testupload ausgewählt.");
            return null;
        }
        if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _errorOutput.DisplayError("Bitte eine PDF-Datei für den SAP-Testupload auswählen.");
            return null;
        }
        return filePath;
    }

    private string? ResolveSapTestBarcode(SapArchiveProfileSettings settings, string filePath)
    {
        return settings.BarcodeSource switch
        {
            BarcodeSource.Fixed => settings.FixedBarcode?.Trim(),
            BarcodeSource.FromFilename => ExtractSapBarcodeWithRegex(Path.GetFileNameWithoutExtension(filePath), settings.BarcodeRegex),
            _ => PromptForSapTestObjectKey(Path.GetFileName(filePath))
        };
    }

    private string? PromptForSapTestObjectKey(string fileName)
    {
        var form = new SapObjectKeyPromptForm(Config, fileName);
        form.ShowModal(this);
        return form.ObjectKey;
    }

    private static string? ExtractSapBarcodeWithRegex(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var match = System.Text.RegularExpressions.Regex.Match(value, pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }
        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success)
            {
                return match.Groups[i].Value.Trim();
            }
        }
        return match.Value.Trim();
    }

    private void UpdateSharePointControlsEnabled()
    {
        bool enabled = _enableSharePointUpload.IsChecked() && !_scanProfile.IsLocked;
        _sharePointSiteUrl.Enabled = enabled;
        _sharePointLibraryPath.Enabled = enabled;
        _sharePointFolderPath.Enabled = enabled;
        _azureAdTenantId.Enabled = enabled;
        _azureAdClientId.Enabled = enabled;
        _azureAdClientSecret.Enabled = enabled;
    }

    private void EnableSharePointUpload_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateSharePointControlsEnabled();
    }

    private void PaperSource_SelectedItemChanged(object? sender, EventArgs e)
    {
        if (_suppressChangeEvent) return;
        UpdateUiForCaps();
    }

    private void AutoSaveSettings_LinkClicked(object? sender, EventArgs eventArgs)
    {
        if (Config.Get(c => c.DisableAutoSave))
        {
            return;
        }
        var form = FormFactory.Create<AutoSaveSettingsForm>();
        ScanProfile.DriverName = DeviceDriver.ToString().ToLowerInvariant();
        ScanProfile.EnableAutoSave = _enableAutoSave.IsChecked();
        // Push the not-yet-saved target selection across so the auto save dialog reports the targets the
        // operator just ticked here rather than the ones last written to disk.
        SyncUploadTargetsToProfile();
        form.ScanProfile = ScanProfile;
        form.ShowModal();
    }

    private void Advanced_Click(object? sender, EventArgs e)
    {
        var form = FormFactory.Create<AdvancedProfileForm>();
        ScanProfile.DriverName = DeviceDriver.ToString().ToLowerInvariant();
        ScanProfile.BitDepth = _bitDepth.SelectedItem;
        form.ScanProfile = ScanProfile;
        form.ShowModal();
    }

    private void EnableAutoSave_CheckedChanged(object? sender, EventArgs e)
    {
        if (!_suppressChangeEvent)
        {
            if (_enableAutoSave.IsChecked())
            {
                _autoSaveSettings.Enabled = true;
                ScanProfile.EnableAutoSave = true;
                var form = FormFactory.Create<AutoSaveSettingsForm>();
                form.ScanProfile = ScanProfile;
                form.ShowModal();
                if (!form.Result)
                {
                    _enableAutoSave.Checked = false;
                }
            }
        }
        _autoSaveSettings.Enabled = _enableAutoSave.IsChecked();
    }
}