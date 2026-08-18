using System;

namespace NAPS2.Sap;

/// <summary>
/// Legacy connection mode retained for compatibility with older ScanMe integration code.
/// </summary>
public enum ConnectionMode
{
    Rfc,
    HttpContentServer
}

/// <summary>
/// Legacy connection insert mode retained for compatibility with older ScanMe integration code.
/// </summary>
public enum ConnectionInsertMode
{
    StandardRfc,
    CustomRfc
}

/// <summary>
/// Stores global SAP OData connection settings for ArchiveLink upload.
/// </summary>
public class SapConnectionConfig : IEquatable<SapConnectionConfig>
{
    /// <summary>
    /// Gets or sets the display name of this connection, for example <c>PRD - Production</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the SAP host including scheme and optional port, without trailing slash.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the SAP OData service name, for example <c>ZARCHIVE_UPLOAD_SRV</c>.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Gets or sets the SAP client, for example <c>100</c>.
    /// </summary>
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the SAP logon language.
    /// </summary>
    public string? Language { get; set; } = "DE";

    /// <summary>
    /// Gets or sets the SAP user name.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Gets or sets the DPAPI-protected password. Plain text passwords must never be stored here.
    /// </summary>
    public string? EncryptedPassword { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TLS certificate validation errors should be ignored. Intended only for test environments.
    /// </summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>
    /// Legacy property retained for compatibility. OData upload ignores this value.
    /// </summary>
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.HttpContentServer;

    /// <summary>
    /// Legacy property retained for compatibility. Prefer <see cref="ServiceName" /> for OData.
    /// </summary>
    public string? SystemId { get; set; }

    /// <summary>
    /// Legacy property retained for compatibility. Prefer <see cref="Host" /> for OData.
    /// </summary>
    public string? AppServerHost
    {
        get => Host;
        set => Host = value;
    }

    /// <summary>
    /// Legacy property retained for compatibility. OData upload ignores this value.
    /// </summary>
    public string? SystemNumber { get; set; }

    /// <summary>
    /// Legacy property retained for compatibility. Maps to <see cref="Host" />.
    /// </summary>
    public string? ContentServerBaseUrl
    {
        get => Host;
        set => Host = value;
    }

    /// <summary>
    /// Legacy property retained for compatibility. When set to true, ensures the host starts with HTTPS only by convention.
    /// </summary>
    public bool UseHttps { get; set; } = true;

    /// <summary>
    /// Legacy property retained for compatibility. OData upload ignores this value.
    /// </summary>
    public ConnectionInsertMode ConnectionInsertMode { get; set; } = ConnectionInsertMode.StandardRfc;

    /// <summary>
    /// Legacy property retained for compatibility. OData upload ignores this value.
    /// </summary>
    public string? CustomRfcName { get; set; }

    /// <summary>
    /// Builds the SAP OData base service URL.
    /// </summary>
    /// <returns>The base service URL.</returns>
    public string GetBaseServiceUrl()
    {
        return $"{(Host ?? string.Empty).TrimEnd('/')}/sap/opu/odata/sap/{Uri.EscapeDataString(ServiceName ?? string.Empty)}";
    }

    /// <summary>
    /// Builds the SAP OData service root URL used first to fetch a CSRF token.
    /// </summary>
    /// <returns>The service root URL including SAP client/language query.</returns>
    public string GetRootUrl()
    {
        return $"{GetBaseServiceUrl()}/?{BuildQuery()}";
    }

    /// <summary>
    /// Builds the SAP OData metadata URL used to fetch a CSRF token.
    /// </summary>
    /// <returns>The metadata URL.</returns>
    public string GetMetadataUrl()
    {
        return $"{GetBaseServiceUrl()}/$metadata?{BuildQuery()}";
    }

    /// <summary>
    /// Builds the SAP OData AttachmentSet upload URL.
    /// </summary>
    /// <returns>The upload URL.</returns>
    public string GetUploadUrl()
    {
        return $"{GetBaseServiceUrl()}/AttachmentSet?{BuildQuery()}";
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SapConnectionConfig);

    /// <inheritdoc />
    public bool Equals(SapConnectionConfig? other)
    {
        return other != null &&
               string.Equals(Name, other.Name, StringComparison.Ordinal) &&
               string.Equals(Host, other.Host, StringComparison.Ordinal) &&
               string.Equals(ServiceName, other.ServiceName, StringComparison.Ordinal) &&
               string.Equals(Client, other.Client, StringComparison.Ordinal) &&
               string.Equals(Language, other.Language, StringComparison.Ordinal) &&
               string.Equals(User, other.User, StringComparison.Ordinal) &&
               string.Equals(EncryptedPassword, other.EncryptedPassword, StringComparison.Ordinal) &&
               IgnoreCertificateErrors == other.IgnoreCertificateErrors;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = AddHash(hash, Name);
            hash = AddHash(hash, Host);
            hash = AddHash(hash, ServiceName);
            hash = AddHash(hash, Client);
            hash = AddHash(hash, Language);
            hash = AddHash(hash, User);
            hash = AddHash(hash, EncryptedPassword);
            hash = hash * 31 + IgnoreCertificateErrors.GetHashCode();
            return hash;
        }
    }

    private string BuildQuery()
    {
        return $"sap-client={Uri.EscapeDataString(Client ?? string.Empty)}&sap-language={Uri.EscapeDataString(string.IsNullOrWhiteSpace(Language) ? "DE" : Language!)}";
    }

    private static int AddHash(int hash, string? value)
    {
        unchecked
        {
            return hash * 31 + StringComparer.Ordinal.GetHashCode(value ?? string.Empty);
        }
    }
}
