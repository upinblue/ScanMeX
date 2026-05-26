using System;

namespace NAPS2.Sap;

/// <summary>
/// Creates SAP ArchiveLink uploaders for the configured transport mode.
/// </summary>
public static class SapArchiveUploaderFactory
{
    /// <summary>
    /// Creates an uploader implementation for the supplied connection configuration.
    /// </summary>
    /// <param name="connection">The SAP connection configuration.</param>
    /// <returns>An ArchiveLink uploader.</returns>
    public static ISapArchiveUploader Create(SapConnectionConfig connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return connection.ConnectionMode switch
        {
            ConnectionMode.Rfc => new RfcSapArchiveUploader(),
            ConnectionMode.HttpContentServer => new HttpSapArchiveUploader(HttpSapArchiveUploader.CreateHttpClient(connection.IgnoreCertificateErrors)),
            _ => throw new ArgumentOutOfRangeException(nameof(connection), connection.ConnectionMode, "Unsupported SAP ArchiveLink connection mode.")
        };
    }
}
