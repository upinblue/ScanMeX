using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport;
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
    private readonly Button _advanced = new() { Text = UiStrings.Advanced };
    private readonly SliderWithTextBox _brightnessSlider = new();
    private readonly SliderWithTextBox _contrastSlider = new();

    // Documents & barcodes -- everything that used to live behind the "auto save settings" link. Keeping
    // it in a second dialog meant the settings that decide what a document is were somewhere else from
    // the settings that decide where it goes, and the two could contradict each other unnoticed.
    private readonly RadioButton _sepOnePerScan;
    private readonly RadioButton _sepOnePerPage;
    private readonly RadioButton _sepPatchT;
    private readonly RadioButton _sepBarcode;
    private readonly CheckBox _symbologyCode39 = new() { Text = UiStrings.BarcodeTypeCode39 };
    private readonly CheckBox _symbologyCode128 = new() { Text = UiStrings.BarcodeTypeCode128 };
    private readonly CheckBox _symbologyEanUpc = new() { Text = UiStrings.BarcodeTypeEanUpc };
    private readonly Label _eanUpcWarning = C.Label(UiStrings.EanUpcPhantomWarning);
    // A label that starts empty has no height, and setting its text later doesn't give it any until the
    // layout is redone. Both warnings therefore carry their text from the start and are shown or hidden.
    private readonly LayoutVisibility _eanUpcWarningVis = new(false);
    private readonly TextBox _separationPattern = new();
    private readonly CheckBox _keepSeparatorPage = new() { Text = UiStrings.KeepSeparatorPage };
    private readonly CheckBox _newDocumentOnlyOnValueChange =
        new() { Text = UiStrings.NewDocumentOnlyOnValueChange };
    private readonly LayoutVisibility _barcodeOptionsVis = new(false);

    private readonly DropDownWidget<DocumentIdMode> _idMode = new();
    private readonly TextBox _idPromptLabel = new();
    private readonly LayoutVisibility _idPromptLabelVis = new(false);
    private readonly CheckBox _requireIdentifier = new() { Text = UiStrings.RequireIdentifier };

    private readonly TextBox _documentName = new();
    private readonly LinkButton _documentNamePlaceholders = C.Link(UiStrings.Placeholders);
    private readonly CheckBox _saveLocally = new() { Text = UiStrings.SaveLocally };
    private readonly FilePathWithPlaceholders _localFolder;
    private readonly CheckBox _promptForFilePath = new() { Text = UiStrings.PromptForFilePath };
    private readonly LayoutVisibility _localFolderVis = new(false);

    private readonly DropDownWidget<UploadTrigger> _uploadTrigger = new();
    private readonly CheckBox _cleanupAfterCompletion = new() { Text = UiStrings.CleanupAfterCompletion };
    // A profile with no destination at all scans and then keeps nothing, which nothing in the scan
    // window reports. This line says so where the mistake is made.
    private readonly Label _noDestinationWarning = C.Label(UiStrings.NoDestinationWarning);
    private readonly LayoutVisibility _noDestinationWarningVis = new(false);

    // SharePoint upload controls
    private readonly CheckBox _enableSharePointUpload = new() { Text = UiStrings.EnableSharePointUpload };
    private readonly TextBox _sharePointSiteUrl = new();
    private readonly TextBox _sharePointLibraryPath = new();
    private readonly TextBox _sharePointFolderPath = new();
    private readonly TextBox _azureAdTenantId = new();
    private readonly TextBox _azureAdClientId = new();
    private readonly PasswordBox _azureAdClientSecret = new();

    // SAP ArchiveLink controls
    private readonly CheckBox _enableSapArchiveUpload = new() { Text = UiStrings.SapEnableUpload };
    private readonly TextBox _sapHost = new();
    private readonly TextBox _sapServiceName = new();
    private readonly TextBox _sapClient = new();
    private readonly TextBox _sapLanguage = new();
    private readonly TextBox _sapUser = new();
    // Always starts empty and only overwrites the stored password when something is typed, so the box
    // being blank has to read as "unchanged" rather than "no password set".
    private readonly PasswordBox _sapPassword = new() { ToolTip = UiStrings.SapPasswordKeepHint };
    private readonly CheckBox _sapIgnoreSsl = new() { Text = UiStrings.SapIgnoreSslCertificateCheck };
    private readonly DropDownWidget<SapObjectTypeCatalogEntry> _sapObjectType = new();
    private readonly TextBox _sapArchiveId = new();
    private readonly TextBox _sapDocumentType = new();
    private readonly RadioButton _sapPromptObjectKey = new() { Text = UiStrings.SapObjectKeyPromptEachScan };
    private readonly RadioButton _sapBarcodeObjectKey;
    private readonly RadioButton _sapFilenameObjectKey;
    private readonly RadioButton _sapFixedObjectKey;
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
        IIconProvider iconProvider, DialogHelper dialogHelper) : base(config)
    {
        Title = UiStrings.EditProfileFormTitle;
        IconName = "blueprints_small";

        _errorOutput = errorOutput;
        _profileNameTracker = profileNameTracker;
        _deviceCapsCache = deviceCapsCache;
        _localFolder = new FilePathWithPlaceholders(this, dialogHelper);
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

        _advanced.Click += Advanced_Click;

        _enableSharePointUpload.CheckedChanged += EnableSharePointUpload_CheckedChanged;

        _sepOnePerScan = new RadioButton { Text = UiStrings.SeparationModeOnePerScan };
        _sepOnePerPage = new RadioButton(_sepOnePerScan) { Text = UiStrings.SeparationModeOnePerPage };
        _sepPatchT = new RadioButton(_sepOnePerScan) { Text = UiStrings.SeparationModePatchT };
        _sepBarcode = new RadioButton(_sepOnePerScan) { Text = UiStrings.SeparationModeBarcode };
        foreach (var button in new[] { _sepOnePerScan, _sepOnePerPage, _sepPatchT, _sepBarcode })
        {
            button.CheckedChanged += (_, _) => UpdateDocumentControls();
        }
        foreach (var box in new[] { _symbologyCode39, _symbologyCode128, _symbologyEanUpc })
        {
            box.CheckedChanged += (_, _) => UpdateDocumentControls();
        }
        _idMode.Format = x => x switch
        {
            DocumentIdMode.Barcode => UiStrings.DocumentIdModeBarcode,
            DocumentIdMode.ManualInput => UiStrings.DocumentIdModeManual,
            _ => UiStrings.DocumentIdModeNone
        };
        _idMode.Items = EnumDropDownWidget<DocumentIdMode>.DefaultItems;
        _idMode.SelectedItemChanged += (_, _) => UpdateDocumentControls();
        _uploadTrigger.Format = x => x == UploadTrigger.Manual
            ? UiStrings.UploadTriggerManual
            : UiStrings.UploadTriggerAutomatic;
        _uploadTrigger.Items = EnumDropDownWidget<UploadTrigger>.DefaultItems;
        _saveLocally.CheckedChanged += (_, _) => UpdateDocumentControls();
        _documentNamePlaceholders.Click += (_, _) => EditDocumentName();

        _sapBarcodeObjectKey = new RadioButton(_sapPromptObjectKey) { Text = UiStrings.SapObjectKeyFromBarcode };
        _sapFilenameObjectKey = new RadioButton(_sapPromptObjectKey) { Text = UiStrings.SapObjectKeyFromFilename };
        _sapFixedObjectKey = new RadioButton(_sapPromptObjectKey) { Text = UiStrings.SapObjectKeyFixedValue };
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
        // Resizable in both directions, and started at a size that fits the densest tab rather than at
        // whatever the widest row happens to measure. The old form sized itself to its content, which is
        // how a settings dialog ends up wider than the screen it is configuring.
        FormStateController.AutoLayoutSize = false;
        FormStateController.FixedHeightLayout = false;
        FormStateController.DefaultClientSize = new Size(560, 640);
        EtoPlatform.Current.AttachDpiDependency(this, scale =>
            MinimumSize = Size.Round(new SizeF(480, 460) * scale));

        LayoutController.Content = L.Column(
            L.Tabs(
                (UiStrings.ProfileTabScanner, ScannerTab()),
                (UiStrings.ProfileTabDocuments, DocumentsTab()),
                (UiStrings.SharePointUpload, SharePointTab()),
                (UiStrings.SapArchiveLink, SapTab())
            ),
            L.Row(
                _advanced,
                C.Filler(),
                L.OkCancel(
                    C.OkButton(this, SaveSettings),
                    C.CancelButton(this))
            )
        );

        UpdateDocumentControls();
    }

    /// <summary>
    /// The widest a single-line text field is allowed to get. A site URL and a SAP client number were
    /// both being stretched to the full width of the dialog, which is what made the dialog wide: a row
    /// of controls that all scale has no natural width to settle at.
    /// </summary>
    private const int FIELD_WIDTH = 320;

    /// <summary>
    /// The width explanatory sentences wrap at. Measured against the dialog's minimum width, so a hint
    /// that fits here fits however the operator has sized the window.
    /// </summary>
    private const int HINT_WRAP_WIDTH = 440;

    private LayoutElement ScannerTab() => L.Scrollable(L.Column(
        C.Label(UiStrings.DisplayNameLabel),
        _displayName.MaxWidth(FIELD_WIDTH),
        C.Spacer(),
        _deviceSelectorWidget,
        C.Spacer(),
        PlatformCompat.System.IsWiaDriverSupported || PlatformCompat.System.IsTwainDriverSupported
            ? L.Row(_predefinedSettings, _nativeUi).Visible(_nativeUiVis)
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
        ),
        C.Filler()
    ).Padding(10));

    /// <summary>
    /// What a document is, what it is called, and when it leaves. These three used to be split between
    /// this dialog and a second one behind a link, which is how a profile could end up uploading to SAP
    /// while separating by a rule nobody looking at it could see.
    /// </summary>
    private LayoutElement DocumentsTab() => L.Scrollable(L.Column(
        C.BodyStrong(UiStrings.SeparationModeLabel),
        _sepOnePerScan,
        _sepOnePerPage,
        _sepPatchT,
        _sepBarcode,
        L.Column(
            C.Spacer(),
            C.Label(UiStrings.BarcodeTypesLabel),
            L.Row(_symbologyCode39, _symbologyCode128, _symbologyEanUpc),
            L.Column(_eanUpcWarning.DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH)).Visible(_eanUpcWarningVis),
            C.Label(UiStrings.SeparationPatternLabel),
            _separationPattern.MaxWidth(FIELD_WIDTH),
            C.Label(UiStrings.SeparationPatternHint).DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH),
            _keepSeparatorPage,
            _newDocumentOnlyOnValueChange,
            C.Label(UiStrings.NewDocumentOnlyOnValueChangeHint).DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH)
        ).Visible(_barcodeOptionsVis),

        C.Spacer(),
        C.BodyStrong(UiStrings.DocumentIdModeLabel),
        _idMode.AsControl().MaxWidth(FIELD_WIDTH),
        L.Column(
            C.Label(UiStrings.DocumentIdPromptLabelLabel),
            _idPromptLabel.MaxWidth(FIELD_WIDTH)
        ).Visible(_idPromptLabelVis),
        _requireIdentifier,

        C.Spacer(),
        C.BodyStrong(UiStrings.DocumentNameSection),
        L.Row(_documentName.MaxWidth(FIELD_WIDTH), _documentNamePlaceholders.AlignCenter()),
        C.Label(UiStrings.DocumentNameHint).DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH),

        C.Spacer(),
        C.BodyStrong(UiStrings.DocumentDestinationSection),
        _saveLocally,
        L.Column(
            C.Label(UiStrings.LocalFolderLabel),
            _localFolder,
            _promptForFilePath
        ).Visible(_localFolderVis),
        C.Spacer(),
        C.Label(UiStrings.UploadTriggerLabel),
        _uploadTrigger.AsControl().MaxWidth(FIELD_WIDTH),
        L.Column(_noDestinationWarning.DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH)).Visible(_noDestinationWarningVis),
        C.Spacer(),
        _cleanupAfterCompletion,
        C.Filler()
    ).Padding(10));

    private LayoutElement SharePointTab() => L.Scrollable(L.Column(
        _enableSharePointUpload,
        C.Spacer(),
        C.Label(UiStrings.SharePointSiteUrlLabel),
        _sharePointSiteUrl.MaxWidth(FIELD_WIDTH),
        C.Label(UiStrings.SharePointLibraryPathLabel),
        _sharePointLibraryPath.MaxWidth(FIELD_WIDTH),
        C.Label(UiStrings.SharePointFolderPathLabel),
        _sharePointFolderPath.MaxWidth(FIELD_WIDTH),
        C.Spacer(),
        C.Label(UiStrings.AzureAdTenantIdLabel),
        _azureAdTenantId.MaxWidth(FIELD_WIDTH),
        C.Label(UiStrings.AzureAdClientIdLabel),
        _azureAdClientId.MaxWidth(FIELD_WIDTH),
        C.Label(UiStrings.AzureAdClientSecretLabel),
        _azureAdClientSecret.MaxWidth(FIELD_WIDTH),
        C.Filler()
    ).Padding(10));

    private LayoutElement SapTab() => L.Scrollable(L.Column(
        _enableSapArchiveUpload,
        C.Spacer(),
        C.BodyStrong(UiStrings.SapConnectionSection),
        L.Row(
            L.Column(
                C.Label(UiStrings.SapHostLabel),
                _sapHost,
                C.Label(UiStrings.SapClientLabel),
                _sapClient,
                C.Label(UiStrings.SapUserLabel),
                _sapUser
            ).Scale(),
            L.Column(
                C.Label(UiStrings.SapServiceNameLabel),
                _sapServiceName,
                C.Label(UiStrings.SapLanguageLabel),
                _sapLanguage,
                C.Label(UiStrings.SapPasswordLabel),
                _sapPassword
            ).Scale()
        ),
        C.Secondary(UiStrings.SapPasswordKeepHint),
        _sapIgnoreSsl,
        C.Spacer(),
        C.BodyStrong(UiStrings.SapArchiveSection),
        L.Row(
            L.Column(
                C.Label(UiStrings.SapArchiveIdLabel),
                _sapArchiveId,
                C.Label(UiStrings.SapArObjectLabel),
                _sapObjectType,
                C.Label(UiStrings.SapObjectLabel),
                _sapDocumentType
            ).Scale(),
            L.Column(
                C.Label(UiStrings.SapObjectIdLabel),
                _sapDescriptionTemplate,
                C.Spacer(),
                C.None()
            ).Scale()
        ),
        C.Spacer(),
        C.BodyStrong(UiStrings.SapObjectKeySourceLabel),
        _sapPromptObjectKey,
        _sapBarcodeObjectKey,
        _sapFilenameObjectKey,
        _sapFixedObjectKey,
        _sapFixedObjectKeyValue.MaxWidth(FIELD_WIDTH),
        C.Spacer(),
        C.Label(UiStrings.SapObjectKeyFromSeparatorInfo).DynamicWrap(HINT_WRAP_WIDTH).MaxWidth(HINT_WRAP_WIDTH),
        C.Spacer(),
        _sapTestConnection.AlignLeading(),
        C.Filler()
    ).Padding(10));

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

        LoadDocumentSettings();

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

    /// <summary>
    /// Fills the Documents tab from the profile, migrating a profile written before saving, uploading and
    /// the upload trigger became separate settings.
    /// </summary>
    private void LoadDocumentSettings()
    {
        var workflow = DocumentWorkflowSettings.ForProfile(ScanProfile);
        switch (workflow.SeparationMode)
        {
            case DocumentSeparationMode.OnePerPage: _sepOnePerPage.Checked = true; break;
            case DocumentSeparationMode.PatchT: _sepPatchT.Checked = true; break;
            case DocumentSeparationMode.Barcode: _sepBarcode.Checked = true; break;
            default: _sepOnePerScan.Checked = true; break;
        }
        _symbologyCode39.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.Code39);
        _symbologyCode128.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.Code128);
        _symbologyEanUpc.Checked = workflow.BarcodeSymbologies.Contains(BarcodeSymbology.EanUpc);
        _separationPattern.Text = workflow.SeparationPattern ?? "";
        _keepSeparatorPage.Checked = workflow.KeepSeparatorPage;
        _newDocumentOnlyOnValueChange.Checked = workflow.NewDocumentOnlyOnValueChange;

        _idMode.SelectedItem = workflow.IdMode;
        _idPromptLabel.Text = workflow.IdPromptLabel ?? "";
        _requireIdentifier.Checked = workflow.RequireIdentifier;

        _documentName.Text = workflow.GetDocumentNameTemplate();
        _saveLocally.Checked = workflow.SaveLocally;
        _localFolder.Text = workflow.LocalFolder ?? "";
        _promptForFilePath.Checked = workflow.PromptForFilePath;
        _uploadTrigger.SelectedItem = workflow.UploadTrigger;
        _cleanupAfterCompletion.Checked = workflow.CleanupAfterCompletion;
    }

    private DocumentWorkflowSettings BuildDocumentWorkflow(string? separationPattern)
    {
        var symbologies = new List<BarcodeSymbology>();
        if (_symbologyCode39.IsChecked()) symbologies.Add(BarcodeSymbology.Code39);
        if (_symbologyCode128.IsChecked()) symbologies.Add(BarcodeSymbology.Code128);
        if (_symbologyEanUpc.IsChecked()) symbologies.Add(BarcodeSymbology.EanUpc);

        return new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = _sepBarcode.Checked ? DocumentSeparationMode.Barcode
                : _sepPatchT.Checked ? DocumentSeparationMode.PatchT
                : _sepOnePerPage.Checked ? DocumentSeparationMode.OnePerPage
                : DocumentSeparationMode.None,
            BarcodeSymbologies = symbologies,
            SeparationPattern = separationPattern,
            KeepSeparatorPage = _keepSeparatorPage.IsChecked(),
            NewDocumentOnlyOnValueChange = _newDocumentOnlyOnValueChange.IsChecked(),
            IdMode = _idMode.SelectedItem,
            IdPromptLabel = string.IsNullOrWhiteSpace(_idPromptLabel.Text) ? null : _idPromptLabel.Text!.Trim(),
            RequireIdentifier = _requireIdentifier.IsChecked(),
            SaveLocally = _saveLocally.IsChecked(),
            LocalFolder = string.IsNullOrWhiteSpace(_localFolder.Text) ? null : _localFolder.Text!.Trim(),
            DocumentNameTemplate = string.IsNullOrWhiteSpace(_documentName.Text)
                ? null
                : _documentName.Text!.Trim(),
            PromptForFilePath = _promptForFilePath.IsChecked(),
            UploadTrigger = _uploadTrigger.SelectedItem,
            CleanupAfterCompletion = _cleanupAfterCompletion.IsChecked()
        };
    }

    /// <summary>
    /// Shows only the settings that are in force, and says so where a combination cannot work.
    /// </summary>
    private void UpdateDocumentControls()
    {
        // Shown when the profile actually reads barcodes -- either to split documents or to identify
        // them. Not merely when something happens to be configured, which meant the section stayed on
        // screen for profiles that had no use for it. Tying it to separation alone would be too narrow
        // the other way: a profile that doesn't split but names its documents after a barcode still has
        // to pick the symbologies, and without them nothing is decoded at all.
        var usesBarcodes = _sepBarcode.Checked || _idMode.SelectedItem == DocumentIdMode.Barcode;
        _barcodeOptionsVis.IsVisible = usesBarcodes;
        // Only separation reads these two, so they don't invite changes when nothing separates. Patch-T
        // sheets are reusable blank cards and are never part of the document, hence no choice there.
        _keepSeparatorPage.Enabled = _sepBarcode.Checked;
        _newDocumentOnlyOnValueChange.Enabled = _sepBarcode.Checked;

        _eanUpcWarningVis.IsVisible = _symbologyEanUpc.IsChecked();
        _eanUpcWarning.TextColor = EtoPlatform.Current.ColorScheme.CautionColor;

        _idPromptLabelVis.IsVisible = _idMode.SelectedItem == DocumentIdMode.ManualInput;
        _localFolderVis.IsVisible = _saveLocally.IsChecked();

        UpdateNoDestinationWarning();
        LayoutController.Invalidate();
    }

    private void UpdateNoDestinationWarning()
    {
        var hasDestination = _saveLocally.IsChecked() || _enableSharePointUpload.IsChecked() ||
                             _enableSapArchiveUpload.IsChecked();
        _noDestinationWarningVis.IsVisible = !hasDestination;
        _noDestinationWarning.TextColor = EtoPlatform.Current.ColorScheme.CautionColor;
    }

    /// <summary>
    /// Opens the placeholder helper on the file name, which is where <c>$(id)</c> and the barcode
    /// variables are documented.
    /// </summary>
    private void EditDocumentName()
    {
        var form = FormFactory.Create<PlaceholdersForm>();
        form.FileName = _documentName.Text;
        form.ShowModal();
        if (form.Updated)
        {
            _documentName.Text = form.FileName;
        }
    }

    /// <summary>
    /// Refuses the combinations that would quietly produce nothing, or the wrong thing.
    /// </summary>
    private bool ValidateDocumentSettings()
    {
        // With no symbology the detector is refused rather than let loose on every format it knows, so a
        // profile that separates by barcode without picking one would simply never separate.
        if (_sepBarcode.Checked && !_symbologyCode39.IsChecked() && !_symbologyCode128.IsChecked() &&
            !_symbologyEanUpc.IsChecked())
        {
            _errorOutput.DisplayError(UiStrings.BarcodeTypeRequired);
            return false;
        }

        var pattern = _separationPattern.Text?.Trim();
        if (!string.IsNullOrEmpty(pattern))
        {
            try
            {
                _ = new Regex(pattern);
            }
            catch (Exception)
            {
                _errorOutput.DisplayError(UiStrings.InvalidSeparationPattern);
                return false;
            }
        }

        if (_saveLocally.IsChecked() && string.IsNullOrWhiteSpace(_localFolder.Text) &&
            !_promptForFilePath.IsChecked())
        {
            _errorOutput.DisplayError(UiStrings.LocalFolderRequired);
            return false;
        }

        // The name is not only a local matter: it is what SharePoint and the SAP archive store the
        // document under, so a profile that uploads still has to have one.
        if (string.IsNullOrWhiteSpace(_documentName.Text))
        {
            _errorOutput.DisplayError(UiStrings.DocumentNameRequired);
            return false;
        }
        return true;
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

        if (!ValidateDocumentSettings())
        {
            return false;
        }

        var sapValidation = BuildSapArchiveSettings().Validate();
        if (sapValidation.Count > 0)
        {
            _errorOutput.DisplayError(DescribeSapProblems(sapValidation));
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
        var pattern = _separationPattern.Text?.Trim();
        var workflow = BuildDocumentWorkflow(string.IsNullOrEmpty(pattern) ? null : pattern);
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

            DocumentWorkflow = workflow,
            // The legacy pair is kept in step with the workflow rather than edited directly. Nothing in
            // the scan window reads it any more, but the command line scanner still does, and a profile
            // that behaves differently from the CLI than from the window is worse than either.
            EnableAutoSave = workflow.SaveLocally,
            AutoSaveSettings = BuildAutoSaveSettings(workflow),
            SapArchiveSettings = BuildSapArchiveSettings(),
            Quality = ScanProfile.Quality,

            // Settings this dialog doesn't edit. They are only reachable from the advanced dialog, and
            // this method builds a brand new profile, so anything not copied here is silently reset the
            // next time a profile is opened and confirmed.
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

            _saveLocally.Enabled = !locked && !Config.Get(c => c.DisableAutoSave);

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
    /// Mirrors the workflow onto the legacy auto save settings, which is the shape the command line
    /// scanner and older config files still speak. Nothing edits these directly any more.
    /// </summary>
    private AutoSaveSettings BuildAutoSaveSettings(DocumentWorkflowSettings workflow)
    {
        var settings = ScanProfile.AutoSaveSettings ?? new AutoSaveSettings();
        return settings with
        {
            FilePath = Path.Combine(workflow.LocalFolder ?? "", workflow.GetDocumentNameTemplate()),
            PromptForFilePath = workflow.PromptForFilePath,
            ClearImagesAfterSaving = workflow.CleanupAfterCompletion,
            Separator = workflow.SeparationMode switch
            {
                DocumentSeparationMode.Barcode => SaveSeparator.Code39Barcode,
                DocumentSeparationMode.PatchT => SaveSeparator.PatchT,
                DocumentSeparationMode.OnePerPage => SaveSeparator.FilePerPage,
                _ => SaveSeparator.FilePerScan
            },
            Code39SeparationPattern = workflow.SeparationPattern,
            UploadToSharePoint = _enableSharePointUpload.IsChecked(),
            UploadToSap = _enableSapArchiveUpload.IsChecked()
        };
    }

    private SapArchiveProfileSettings BuildSapArchiveSettings()
    {
        var current = ScanProfile.SapArchiveSettings;
        var currentConnection = current?.Connection ?? Config.Get(c => c.SapConnection);
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
            // The profile's one barcode pattern lives on the workflow now; this legacy field is kept so a
            // profile written by this version still reads correctly in an older one.
            BarcodeRegex = _separationPattern.Text?.Trim(),
            FixedBarcode = _sapFixedObjectKeyValue.Text.Trim(),

            // Settings this dialog doesn't edit. A new object is built on every save, so anything not
            // carried across here is emptied the next time the profile is opened and confirmed -- the same
            // trap GetUpdatedScanProfile documents for the profile as a whole.
            ArDocType = current?.ArDocType,
            ConnectionName = current?.ConnectionName,
            SlugTemplate = current?.SlugTemplate,
            BarcodeTemplate = current?.BarcodeTemplate,
            DescriptionTemplate = current?.DescriptionTemplate
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
        _sapFixedObjectKeyValue.Enabled = enabled && _sapFixedObjectKey.Checked;
        _sapDescriptionTemplate.Enabled = enabled;
        _sapTestConnection.Enabled = enabled;
        UpdateNoDestinationWarning();
    }

    private async void SapTestConnection_Click(object? sender, EventArgs e)
    {
        var settings = BuildSapArchiveSettings();
        var validation = settings.Validate();
        if (validation.Count > 0)
        {
            _errorOutput.DisplayError(DescribeSapProblems(validation));
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
        using var uploader = new HttpSapArchiveUploader(request.Connection);
        var result = await uploader.UploadAsync(request, CancellationToken.None);
        MessageBox.Show(this,
            result.Success
                ? string.Format(UiStrings.SapTestUploadSucceeded, result.ArchivDocId, barcode)
                : string.Format(UiStrings.SapTestUploadFailed, result.HttpStatusCode, result.ErrorCode,
                    result.ErrorMessage, result.TransactionId),
            UiStrings.SapArchiveLink,
            MessageBoxButtons.OK,
            result.Success ? MessageBoxType.Information : MessageBoxType.Error);
    }

    /// <summary>
    /// Turns the validation problem codes from ScanMe.Sap into localized messages, one per line.
    /// </summary>
    private static string DescribeSapProblems(IReadOnlyList<SapSettingsIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(DescribeSapProblem));

    private static string DescribeSapProblem(SapSettingsIssue issue) => issue.Problem switch
    {
        SapSettingsProblem.ArchiveIdMissing => UiStrings.SapValidationArchiveIdRequired,
        SapSettingsProblem.HostMissingOrNotHttps => UiStrings.SapValidationHostRequired,
        SapSettingsProblem.ServiceNameMissing => UiStrings.SapValidationServiceNameRequired,
        SapSettingsProblem.ClientNotThreeDigits => UiStrings.SapValidationClientRequired,
        SapSettingsProblem.UserMissing => UiStrings.SapValidationUserRequired,
        SapSettingsProblem.FixedBarcodeMissing => UiStrings.SapValidationFixedBarcodeRequired,
        SapSettingsProblem.BarcodeRegexInvalid =>
            string.Format(UiStrings.SapValidationBarcodeRegexInvalid, issue.Detail),
        _ => issue.Problem.ToString()
    };

    private string? PromptForSapTestPdf()
    {
        var ofd = new OpenFileDialog
        {
            MultiSelect = false,
            CheckFileExists = true,
            Title = UiStrings.SapTestUploadSelectPdf
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
            _errorOutput.DisplayError(UiStrings.SapTestUploadNoPdfSelected);
            return null;
        }
        if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _errorOutput.DisplayError(UiStrings.SapTestUploadNotAPdf);
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
        UpdateNoDestinationWarning();
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

    private void Advanced_Click(object? sender, EventArgs e)
    {
        var form = FormFactory.Create<AdvancedProfileForm>();
        ScanProfile.DriverName = DeviceDriver.ToString().ToLowerInvariant();
        ScanProfile.BitDepth = _bitDepth.SelectedItem;
        form.ScanProfile = ScanProfile;
        form.ShowModal();
    }
}