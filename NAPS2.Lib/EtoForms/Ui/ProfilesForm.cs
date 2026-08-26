using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.ImportExport.Profiles;
using NAPS2.Scan;
using NAPS2.Serialization;

namespace NAPS2.EtoForms.Ui;

public class ProfilesForm : EtoDialogBase
{
    private readonly IScanPerformer _scanPerformer;
    private readonly ProfileNameTracker _profileNameTracker;
    private readonly IProfileManager _profileManager;
    private readonly ThumbnailController _thumbnailController;
    private readonly IIconProvider _iconProvider;
    private readonly ProfileTransfer _profileTransfer = new();
    private readonly ProfileFileTransfer _profileFileTransfer = new();

    private readonly IListView<ScanProfile> _listView;

    private readonly ActionCommand _scanCommand;
    private readonly ActionCommand _addCommand;
    private readonly ActionCommand _editCommand;
    private readonly ActionCommand _deleteCommand;
    private readonly ActionCommand _setDefaultCommand;
    private readonly ActionCommand _copyCommand;
    private readonly ActionCommand _pasteCommand;
    private readonly ActionCommand _exportCommand;
    private readonly ActionCommand _importCommand;
    private readonly ActionCommand _scannerSharingCommand;

    public ProfilesForm(Naps2Config config, IScanPerformer scanPerformer, ProfileNameTracker profileNameTracker,
        IProfileManager profileManager, ProfileListViewBehavior profileListViewBehavior,
        ThumbnailController thumbnailController, IIconProvider iconProvider)
        : base(config)
    {
        Title = UiStrings.ProfilesFormTitle;
        IconName = "blueprints_small";

        _scanPerformer = scanPerformer;
        _profileNameTracker = profileNameTracker;
        _profileManager = profileManager;
        _thumbnailController = thumbnailController;
        _iconProvider = iconProvider;

        // TODO: Do this only in WinForms (?)
        // switch (Handler)
        // {
        //     case IWindowsControl windowsControl:
        //         windowsControl.UseShellDropManager = false;
        //         break;
        // }

        profileListViewBehavior.NoUserProfiles = NoUserProfiles;
        _listView = EtoPlatform.Current.CreateListView(profileListViewBehavior);
        _scanCommand = new ActionCommand(DoScan)
        {
            MenuText = UiStrings.Scan,
            IconName = "control_play_blue_small",
        };
        _addCommand = new ActionCommand(DoAdd)
        {
            MenuText = UiStrings.New,
            IconName = "add_small"
        };
        _editCommand = new ActionCommand(DoEdit)
        {
            MenuText = UiStrings.Edit,
            IconName = "pencil_small"
        };
        _deleteCommand = new ActionCommand(DoDelete)
        {
            MenuText = UiStrings.Delete,
            IconName = "cross_small"
        };
        _setDefaultCommand = new ActionCommand(DoSetDefault)
        {
            MenuText = UiStrings.SetDefault,
            IconName = "accept_small"
        };
        _copyCommand = new ActionCommand(DoCopy)
        {
            MenuText = UiStrings.Copy
        };
        _pasteCommand = new ActionCommand(DoPaste)
        {
            MenuText = UiStrings.Paste
        };
        _exportCommand = new ActionCommand(DoExport)
        {
            MenuText = UiStrings.ProfileExport
        };
        _importCommand = new ActionCommand(DoImport)
        {
            MenuText = UiStrings.ProfileImport
        };
        _scannerSharingCommand = new ActionCommand(OpenScannerSharingForm)
        {
            MenuText = UiStrings.ScannerSharing,
            IconName = "wireless_small"
        };

        var profilesKsm = new KeyboardShortcutManager();
        profilesKsm.Assign("Esc", Close);
        profilesKsm.Assign("Del", _deleteCommand);
        profilesKsm.Assign("Mod+C", _copyCommand);
        profilesKsm.Assign("Mod+V", _pasteCommand);
        EtoPlatform.Current.HandleKeyDown(_listView.Control, profilesKsm.Perform);

        EtoPlatform.Current.AttachDpiDependency(this, _ => _listView.RegenerateImages());
        _listView.ImageSize = new Size(48, 48);
        _listView.ItemClicked += ItemClicked;
        _listView.SelectionChanged += SelectionChanged;
        _listView.Drop += Drop;
        profileManager.ProfilesUpdated += ProfilesUpdated;

        _addCommand.Enabled = !NoUserProfiles;
        _editCommand.Enabled = false;
        _deleteCommand.Enabled = false;
        // Importing adds profiles, so it goes where adding one goes. Exporting only reads, and is
        // enabled from ReloadProfiles as soon as there is anything to write.
        _importCommand.Enabled = !NoUserProfiles;
        _exportCommand.Enabled = false;

        var contextMenu = new ContextMenu();
        _listView.ContextMenu = contextMenu;
        contextMenu.AddItems(
            C.ButtonMenuItem(this, _scanCommand),
            C.ButtonMenuItem(this, _editCommand),
            C.ButtonMenuItem(this, _setDefaultCommand),
            new SeparatorMenuItem());
        if (!NoUserProfiles)
        {
            contextMenu.AddItems(
                C.ButtonMenuItem(this, _copyCommand),
                C.ButtonMenuItem(this, _pasteCommand),
                C.ButtonMenuItem(this, _exportCommand),
                C.ButtonMenuItem(this, _importCommand),
                new SeparatorMenuItem());
        }
        else
        {
            // Exporting is a read, so an installation whose profiles are the administrator's can still
            // hand them on; importing would add one, which is exactly what NoUserProfiles forbids.
            contextMenu.AddItems(
                C.ButtonMenuItem(this, _exportCommand),
                new SeparatorMenuItem());
        }
        contextMenu.AddItems(
            C.ButtonMenuItem(this, _deleteCommand));
        contextMenu.Opening += ContextMenuOpening;
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Size(200, 0);

        LayoutController.Content = L.Column(
            L.Row(
                _listView.Control.NaturalSize(150, 100).Scale(),
                C.Button(_scanCommand, "control_play_blue", ButtonImagePosition.Above, ButtonFlags.LargeIcon)
                    .Height(80)
            ).Aligned().Scale(),
            L.Row(
                L.Column(
                    L.Row(
                        C.Button(_addCommand, ButtonImagePosition.Left),
                        C.Button(_editCommand, ButtonImagePosition.Left),
                        C.Button(_deleteCommand, ButtonImagePosition.Left),
                        C.Button(_importCommand),
                        C.Button(_exportCommand),
                        C.Filler(),
                        Config.Get(c => c.DisableScannerSharing)
                            ? C.None()
                            : C.Button(_scannerSharingCommand, ButtonImagePosition.Left)
                    )
                ),
                C.CancelButton(this, UiStrings.Done)
            ).Aligned());
    }

    public Action<ProcessedImage>? ImageCallback { get; set; }

    private ScanProfile? SelectedProfile => _listView.Selection.SingleOrDefault();

    private bool SelectionLocked
    {
        get { return _listView.Selection.Any(x => x.IsLocked); }
    }

    private bool NoUserProfiles => Config.Get(c => c.NoUserProfiles) && _profileManager.Profiles.Any(x => x.IsLocked);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ReloadProfiles();
        var defaultProfile = _profileManager.Profiles.FirstOrDefault(x => x.IsDefault);
        if (defaultProfile != null)
        {
            _listView.Selection = ListSelection.Of(defaultProfile);
        }
    }

    private void ProfilesUpdated(object? sender, EventArgs e)
    {
        ReloadProfiles();

        // If we only have one profile, make it the default
        var profiles = _profileManager.Profiles;
        if (profiles.Count == 1 && !profiles[0].IsDefault)
        {
            _profileManager.DefaultProfile = profiles.Single();
        }
    }

    private void ReloadProfiles()
    {
        _listView.SetItems(_profileManager.Profiles);
        _exportCommand.Enabled = _profileManager.Profiles.Count > 0;
    }

    private void SelectionChanged(object? sender, EventArgs e)
    {
        _editCommand.Enabled = _listView.Selection.Count == 1;
        _deleteCommand.Enabled = _listView.Selection.Count > 0 && !SelectionLocked;
    }

    private void ItemClicked(object? sender, EventArgs e)
    {
        if (SelectedProfile != null)
        {
            DoScan();
        }
    }

    private void Drop(object? sender, DropEventArgs e)
    {
        // Receive drop data
        if (e.CustomData != null)
        {
            var data = _profileTransfer.FromBinaryData(e.CustomData);
            if (data.ProcessId == Process.GetCurrentProcess().Id)
            {
                if (data.Locked)
                {
                    return;
                }
                int index = e.Position;
                while (index < _profileManager.Profiles.Count && _profileManager.Profiles[index].IsLocked)
                {
                    index++;
                }
                _profileManager.Mutate(new ListMutation<ScanProfile>.MoveTo(index), _listView);
            }
            else
            {
                if (!NoUserProfiles)
                {
                    _profileManager.Mutate(
                        new ListMutation<ScanProfile>.AppendAndSelect(data.ScanProfileXml.FromXml<ScanProfile>()),
                        _listView);
                }
            }
        }
    }

    private ScanParams DefaultScanParams() =>
        new()
        {
            NoAutoSave = Config.Get(c => c.DisableAutoSave),
            OcrParams = Config.OcrAfterScanningParams(),
            ThumbnailSize = _thumbnailController.RenderSize
        };

    private void ContextMenuOpening(object? sender, EventArgs e)
    {
        _setDefaultCommand.Enabled = SelectedProfile != null && !SelectedProfile.IsDefault;
        _editCommand.Enabled = SelectedProfile != null;
        _deleteCommand.Enabled = SelectedProfile != null && !SelectedProfile.IsLocked;
        _copyCommand.Enabled = SelectedProfile != null;
        _pasteCommand.Enabled = _profileTransfer.IsInClipboard();
        _exportCommand.Enabled = _profileManager.Profiles.Count > 0;
    }

    private async void DoScan()
    {
        if (ImageCallback == null)
        {
            throw new InvalidOperationException("Image callback not specified");
        }
        if (_profileManager.Profiles.Count == 0)
        {
            var editSettingsForm = FormFactory.Create<EditProfileForm>();
            editSettingsForm.NewProfile = true;
            editSettingsForm.ScanProfile = new ScanProfile
            {
                Version = ScanProfile.CURRENT_VERSION
            };
            editSettingsForm.ShowModal();
            if (!editSettingsForm.Result)
            {
                return;
            }
            _profileManager.Mutate(new ListMutation<ScanProfile>.AppendAndSelect(editSettingsForm.ScanProfile),
                _listView);
            _profileManager.DefaultProfile = editSettingsForm.ScanProfile;
        }
        if (SelectedProfile == null)
        {
            MessageBox.Show(MiscResources.SelectProfileBeforeScan, MiscResources.ChooseProfile, MessageBoxButtons.OK,
                MessageBoxType.Warning);
            return;
        }
        if (_profileManager.DefaultProfile == null)
        {
            _profileManager.DefaultProfile = SelectedProfile;
        }
        var images = _scanPerformer.PerformScan(SelectedProfile, DefaultScanParams(), NativeHandle);
        await foreach (var image in images)
        {
            ImageCallback(image);
        }
        Focus();
    }

    private void DoAdd()
    {
        var fedit = FormFactory.Create<EditProfileForm>();
        fedit.NewProfile = true;
        fedit.ScanProfile = Config.DefaultProfileSettings();
        fedit.ShowModal();
        if (fedit.Result)
        {
            _profileManager.Mutate(new ListMutation<ScanProfile>.AppendAndSelect(fedit.ScanProfile), _listView);
        }
    }

    private void DoEdit()
    {
        var originalProfile = SelectedProfile;
        if (originalProfile != null)
        {
            var fedit = FormFactory.Create<EditProfileForm>();
            fedit.ScanProfile = originalProfile;
            fedit.ShowModal();
            if (fedit.Result)
            {
                _profileManager.Mutate(new ListMutation<ScanProfile>.ReplaceWith(fedit.ScanProfile), _listView);
            }
        }
    }

    private void DoDelete()
    {
        if (SelectedProfile != null && !SelectionLocked)
        {
            string message = string.Format(MiscResources.ConfirmDeleteSingleProfile, SelectedProfile.DisplayName);
            if (MessageBox.Show(message, MiscResources.Delete, MessageBoxButtons.OKCancel, MessageBoxType.Warning,
                    MessageBoxDefaultButton.OK) == DialogResult.Ok)
            {
                foreach (var profile in _listView.Selection)
                {
                    _profileNameTracker.DeletingProfile(profile.DisplayName);
                }
                _profileManager.Mutate(new ListMutation<ScanProfile>.DeleteSelected(), _listView);
            }
        }
    }

    private void DoSetDefault()
    {
        if (SelectedProfile != null)
        {
            _profileManager.DefaultProfile = SelectedProfile;
        }
    }

    private void DoCopy()
    {
        if (SelectedProfile != null)
        {
            _profileTransfer.SetClipboard(SelectedProfile);
        }
    }

    private void DoPaste()
    {
        if (NoUserProfiles)
        {
            return;
        }
        if (_profileTransfer.IsInClipboard())
        {
            var data = _profileTransfer.GetFromClipboard();
            var profile = data.ScanProfileXml.FromXml<ScanProfile>();
            _profileManager.Mutate(new ListMutation<ScanProfile>.AppendAndSelect(profile), _listView);
        }
    }

    /// <summary>
    /// Writes profiles to a file that can be carried to another machine. Nothing selected means all of
    /// them: setting a second workstation up is the reason this exists, and that is a whole-list job.
    /// </summary>
    private void DoExport()
    {
        // In list order rather than selection order -- the file is read back as a list of profiles, and
        // the order they are in is the order they were in here.
        var profiles = _listView.Selection.Count > 0
            ? _profileManager.Profiles.Where(x => _listView.Selection.Contains(x)).ToList()
            : _profileManager.Profiles.ToList();
        if (profiles.Count == 0)
        {
            return;
        }

        var sd = new SaveFileDialog
        {
            Title = UiStrings.ProfileExportTitle,
            FileName = DefaultExportFileName(profiles)
        };
        sd.Filters.Add(new FileFilter(UiStrings.ProfileFileType, ProfileFileTransfer.FileExtension, ".xml"));
        sd.Filters.Add(new FileFilter(MiscResources.FileTypeAllFiles, ".*"));
        EtoPlatform.Current.ConfigureFileDialog(sd);
        if (sd.ShowDialog(this) != DialogResult.Ok)
        {
            return;
        }

        try
        {
            _profileFileTransfer.Export(profiles, sd.FileName);
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Error exporting profiles to {sd.FileName}", ex);
            ScanConsole.Profile($"Exporting {profiles.Count} profile(s) to '{sd.FileName}' failed: {ex.Message}");
            MessageBox.Show(string.Format(UiStrings.ProfileExportFailed, ex.Message), UiStrings.ProfileExportTitle,
                MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        bool secretsLeftOut = profiles.Any(ProfileFileTransfer.HasStoredSecret);
        ScanConsole.Profile(
            $"Exported {profiles.Count} profile(s) to '{sd.FileName}': {string.Join(", ", profiles.Select(x => $"'{x.DisplayName}'"))}." +
            (secretsLeftOut ? " The stored SAP password and SharePoint client secret were left out." : ""));
        var message = string.Format(UiStrings.ProfileExportDone, profiles.Count, sd.FileName);
        if (secretsLeftOut)
        {
            message += Environment.NewLine + Environment.NewLine + UiStrings.ProfileExportSecretsLeftOut;
        }
        MessageBox.Show(message, UiStrings.ProfileExportTitle, MessageBoxButtons.OK, MessageBoxType.Information);
    }

    /// <summary>
    /// Reads profiles out of an exported file and appends them, leaving the ones already here alone.
    /// </summary>
    private void DoImport()
    {
        if (NoUserProfiles)
        {
            return;
        }

        var ofd = new OpenFileDialog
        {
            Title = UiStrings.ProfileImportTitle,
            MultiSelect = false,
            CheckFileExists = true
        };
        ofd.Filters.Add(new FileFilter(UiStrings.ProfileFileType, ProfileFileTransfer.FileExtension, ".xml"));
        ofd.Filters.Add(new FileFilter(MiscResources.FileTypeAllFiles, ".*"));
        EtoPlatform.Current.ConfigureFileDialog(ofd);
        if (ofd.ShowDialog(this) != DialogResult.Ok)
        {
            return;
        }
        var path = ofd.Filenames.FirstOrDefault();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        IReadOnlyList<ScanProfile> imported;
        try
        {
            imported = _profileFileTransfer.Import(path);
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Error importing profiles from {path}", ex);
            ScanConsole.Profile($"Importing profiles from '{path}' failed: {ex.Message}");
            MessageBox.Show(string.Format(UiStrings.ProfileImportFailed, ex.Message), UiStrings.ProfileImportTitle,
                MessageBoxButtons.OK, MessageBoxType.Error);
            return;
        }

        if (imported.Count == 0)
        {
            ScanConsole.Profile($"'{path}' holds no profiles; nothing was imported.");
            MessageBox.Show(UiStrings.ProfileImportNone, UiStrings.ProfileImportTitle, MessageBoxButtons.OK,
                MessageBoxType.Warning);
            return;
        }

        var taken = new HashSet<string>(_profileManager.Profiles.Select(x => x.DisplayName));
        var renames = new List<string>();
        ScanProfile? defaultInFile = null;
        foreach (var profile in imported)
        {
            if (profile.IsDefault)
            {
                defaultInFile ??= profile;
                profile.IsDefault = false;
            }
            var unique = ProfileFileTransfer.MakeNameUnique(profile.DisplayName, taken);
            if (unique != null)
            {
                renames.Add($"{profile.DisplayName} -> {unique}");
                profile.DisplayName = unique;
            }
            taken.Add(profile.DisplayName);
        }

        _profileManager.Mutate(new ListMutation<ScanProfile>.AppendAndSelect(imported), _listView);
        // A machine with no default at all asks which profile to scan with every time, so a profile that
        // was the default where it came from becomes the default here -- but only when nothing else is.
        if (defaultInFile != null && _profileManager.DefaultProfile == null)
        {
            _profileManager.DefaultProfile = defaultInFile;
        }

        var needSecret = imported
            .Where(x => ProfileFileTransfer.NeedsSapPassword(x) || ProfileFileTransfer.NeedsSharePointSecret(x))
            .Select(x => x.DisplayName)
            .ToList();
        ScanConsole.Profile(
            $"Imported {imported.Count} profile(s) from '{path}': {string.Join(", ", imported.Select(x => $"'{x.DisplayName}'"))}." +
            (renames.Count > 0 ? $" Renamed: {string.Join(", ", renames)}." : "") +
            (needSecret.Count > 0
                ? $" Still needs a SAP password or SharePoint client secret: {string.Join(", ", needSecret.Select(x => $"'{x}'"))}."
                : ""));
        var message = string.Format(UiStrings.ProfileImportDone, imported.Count);
        if (renames.Count > 0)
        {
            message += Environment.NewLine + Environment.NewLine +
                       string.Format(UiStrings.ProfileImportRenamed, string.Join(", ", renames));
        }
        if (needSecret.Count > 0)
        {
            message += Environment.NewLine + Environment.NewLine +
                       string.Format(UiStrings.ProfileImportSecretsNeeded, string.Join(", ", needSecret));
        }
        MessageBox.Show(message, UiStrings.ProfileImportTitle, MessageBoxButtons.OK, MessageBoxType.Information);
    }

    private static string DefaultExportFileName(IReadOnlyList<ScanProfile> profiles)
    {
        var name = profiles.Count == 1 ? profiles[0].DisplayName : "ScanMe-Profiles";
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return (string.IsNullOrWhiteSpace(name) ? "ScanMe-Profiles" : name) + ProfileFileTransfer.FileExtension;
    }

    private void OpenScannerSharingForm()
    {
        var form = FormFactory.Create<ScannerSharingForm>();
        form.ShowModal();
    }
}