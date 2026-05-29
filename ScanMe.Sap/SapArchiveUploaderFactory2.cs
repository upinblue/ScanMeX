using System;

namespace NAPS2.Sap;

/// <summary>
/// Creates SAP ArchiveLink OData uploaders.
/// </summary>
public static class SapArchiveUploaderFactory
{
    /// <summary>
    /// Creates an OData uploader for the supplied connection configuration.
    /// </summary>
    public static ISapArchiveUploader Create(SapConnectionConfig connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }
        return new HttpSapArchiveUploader(connection);
    }
}
