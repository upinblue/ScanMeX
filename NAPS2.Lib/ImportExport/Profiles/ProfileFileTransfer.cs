using System.Collections.Immutable;
using NAPS2.Config;
using NAPS2.Config.Model;
using NAPS2.Scan;

namespace NAPS2.ImportExport.Profiles;

/// <summary>
/// Writes profiles to a file the operator can carry to another machine, and reads them back. The
/// clipboard <see cref="ProfileTransfer" /> next to this one only reaches another window of the same
/// installation; setting up a second workstation needs something that survives being copied onto a USB
/// stick.
/// </summary>
/// <remarks>
/// The file is a profiles.xml, written and read through <see cref="ProfileSerializer" />: an export can
/// therefore be opened, checked and hand-edited like the real thing, and a profiles.xml lifted straight
/// out of AppData -- including one written by an older version -- can be imported as it stands.
/// </remarks>
internal class ProfileFileTransfer
{
    /// <summary>
    /// The extension the export dialog offers. Nothing depends on it -- the content is what is read --
    /// but it keeps the open dialog from listing every XML file on the machine.
    /// </summary>
    public const string FileExtension = ".scanmeprofiles";

    private readonly ProfileSerializer _serializer = new();

    /// <summary>
    /// Writes the given profiles to a file, without the secrets they hold.
    /// </summary>
    /// <returns>The profiles as they were written, which is what the caller reports on.</returns>
    public IReadOnlyList<ScanProfile> Export(IEnumerable<ScanProfile> profiles, string path)
    {
        var forExport = profiles.Select(WithoutSecrets).ToImmutableList();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        _serializer.Serialize(stream, new ConfigStorage<ImmutableList<ScanProfile>>(forExport));
        return forExport;
    }

    /// <summary>
    /// Reads profiles out of a file. The secrets are dropped here as well as on the way out -- see
    /// <see cref="WithoutSecrets" /> for why a file that carries one still must not install it.
    /// </summary>
    public IReadOnlyList<ScanProfile> Import(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var storage = _serializer.Deserialize(stream);
        if (!storage.TryGet(c => c, out ImmutableList<ScanProfile> profiles) || profiles == null)
        {
            return [];
        }
        return profiles.Select(WithoutSecrets).ToList();
    }

    /// <summary>
    /// A copy of the profile with everything that is a secret, or true only of this machine, taken out.
    /// </summary>
    /// <remarks>
    /// The SAP password is DPAPI-protected for one user on one machine, so elsewhere it is not merely
    /// secret, it is unusable: kept, it would make the password box on the other machine say a password
    /// is stored while every upload fails to decrypt it. The SharePoint client secret is stored in plain
    /// text and would travel readable in a file the operator mails to themselves. Both are dropped in
    /// both directions, so the one rule holds either way round: <b>a profile that crosses a machine
    /// boundary arrives without secrets</b>, and the operator is told which ones need one typed in.
    ///
    /// Locked is a statement about the administrator's profiles file on the machine the profile came
    /// from, not about the profile, so it goes too. The default flag survives the file and is dealt with
    /// on import, where it is known whether this machine already has a default.
    /// </remarks>
    public static ScanProfile WithoutSecrets(ScanProfile profile)
    {
        var copy = profile.Clone();
        copy.IsLocked = false;
        copy.IsDeviceLocked = false;
        if (!string.IsNullOrEmpty(copy.SharePointUploadSettings?.ClientSecret))
        {
            copy.SharePointUploadSettings = copy.SharePointUploadSettings with { ClientSecret = null };
        }
        if (copy.SapArchiveSettings?.Connection != null)
        {
            copy.SapArchiveSettings.Connection.EncryptedPassword = null;
        }
        return copy;
    }

    /// <summary>
    /// Whether this profile holds a secret that an export would leave behind. Asked of the original
    /// rather than of the copy, so the operator is only told about secrets they actually had.
    /// </summary>
    public static bool HasStoredSecret(ScanProfile profile) =>
        !string.IsNullOrEmpty(profile.SharePointUploadSettings?.ClientSecret) ||
        !string.IsNullOrEmpty(profile.SapArchiveSettings?.Connection?.EncryptedPassword);

    /// <summary>
    /// Whether this profile uploads to SAP, and so cannot do anything until a password is entered here.
    /// </summary>
    public static bool NeedsSapPassword(ScanProfile profile) => profile.UploadsToSap();

    /// <summary>
    /// Whether this profile uploads to SharePoint, and so needs its client secret entered here.
    /// </summary>
    public static bool NeedsSharePointSecret(ScanProfile profile) => profile.UploadsToSharePoint();

    /// <summary>
    /// A name that is not already taken, so importing a file twice cannot produce two profiles the
    /// operator has no way of telling apart. Returns null when the name was free.
    /// </summary>
    public static string? MakeNameUnique(string name, ISet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return null;
        }
        for (int i = 2;; i++)
        {
            var candidate = $"{name} ({i})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
