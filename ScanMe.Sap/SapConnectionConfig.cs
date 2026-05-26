using System;

namespace NAPS2.Sap;

/// <summary>
/// Specifies the transport used to connect ScanMe with SAP ArchiveLink infrastructure.
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    /// Uses SAP RFC connectivity. No RFC implementation is included in this project.
    /// </summary>
    Rfc,

    /// <summary>
    /// Uses an SAP HTTP Content Server endpoint.
    /// </summary>
    HttpContentServer
}

/// <summary>
/// Specifies how the ArchiveLink connection row is created after a content-server upload.
/// </summary>
public enum ConnectionInsertMode
{
    /// <summary>
    /// Uses the standard SAP RFC <c>ARCHIV_CONNECTION_INSERT</c>.
    /// </summary>
    StandardRfc,

    /// <summary>
    /// Uses a customer-specific RFC wrapper.
    /// </summary>
    CustomRfc
}

/// <summary>
/// Stores SAP system connection settings for ArchiveLink integration.
/// </summary>
public class SapConnectionConfig : IEquatable<SapConnectionConfig>
{
    /// <summary>
    /// Gets or sets the SAP connection mode.
    /// </summary>
    public ConnectionMode ConnectionMode { get; set; }

    /// <summary>
    /// Gets or sets the SAP system ID, for example <c>PRD</c>.
    /// </summary>
    public string? SystemId { get; set; }

    /// <summary>
    /// Gets or sets the SAP application server host name.
    /// </summary>
    public string? AppServerHost { get; set; }

    /// <summary>
    /// Gets or sets the two-digit SAP system number, for example <c>00</c>.
    /// </summary>
    public string? SystemNumber { get; set; }

    /// <summary>
    /// Gets or sets the SAP client, for example <c>100</c>.
    /// </summary>
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the SAP logon language, for example <c>DE</c> or <c>EN</c>.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the SAP user name.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Gets or sets the DPAPI-protected password. Plain text passwords must never be stored here.
    /// </summary>
    public string? EncryptedPassword { get; set; }

    /// <summary>
    /// Gets or sets the SAP HTTP Content Server base URL used in <see cref="ConnectionMode.HttpContentServer" /> mode.
    /// </summary>
    public string? ContentServerBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS should be required for HTTP Content Server communication.
    /// </summary>
    public bool UseHttps { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether certificate validation errors should be ignored.
    /// </summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>
    /// Gets or sets how the ArchiveLink connection is inserted after HTTP content-server upload.
    /// </summary>
    public ConnectionInsertMode ConnectionInsertMode { get; set; } = ConnectionInsertMode.StandardRfc;

    /// <summary>
    /// Gets or sets the customer-specific RFC name used when <see cref="ConnectionInsertMode" /> is <see cref="ConnectionInsertMode.CustomRfc" />.
    /// </summary>
    public string? CustomRfcName { get; set; }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SapConnectionConfig);

    /// <inheritdoc />
    public bool Equals(SapConnectionConfig? other)
    {
        return other != null &&
               ConnectionMode == other.ConnectionMode &&
               string.Equals(SystemId, other.SystemId, StringComparison.Ordinal) &&
               string.Equals(AppServerHost, other.AppServerHost, StringComparison.Ordinal) &&
               string.Equals(SystemNumber, other.SystemNumber, StringComparison.Ordinal) &&
               string.Equals(Client, other.Client, StringComparison.Ordinal) &&
               string.Equals(Language, other.Language, StringComparison.Ordinal) &&
               string.Equals(User, other.User, StringComparison.Ordinal) &&
               string.Equals(EncryptedPassword, other.EncryptedPassword, StringComparison.Ordinal) &&
               string.Equals(ContentServerBaseUrl, other.ContentServerBaseUrl, StringComparison.Ordinal) &&
               UseHttps == other.UseHttps &&
               IgnoreCertificateErrors == other.IgnoreCertificateErrors &&
               ConnectionInsertMode == other.ConnectionInsertMode &&
               string.Equals(CustomRfcName, other.CustomRfcName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + ConnectionMode.GetHashCode();
            hash = AddHash(hash, SystemId);
            hash = AddHash(hash, AppServerHost);
            hash = AddHash(hash, SystemNumber);
            hash = AddHash(hash, Client);
            hash = AddHash(hash, Language);
            hash = AddHash(hash, User);
            hash = AddHash(hash, EncryptedPassword);
            hash = AddHash(hash, ContentServerBaseUrl);
            hash = hash * 31 + UseHttps.GetHashCode();
            hash = hash * 31 + IgnoreCertificateErrors.GetHashCode();
            hash = hash * 31 + ConnectionInsertMode.GetHashCode();
            hash = AddHash(hash, CustomRfcName);
            return hash;
        }
    }

    private static int AddHash(int hash, string? value)
    {
        unchecked
        {
            return hash * 31 + StringComparer.Ordinal.GetHashCode(value ?? string.Empty);
        }
    }
}
