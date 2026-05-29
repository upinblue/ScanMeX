using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// Lightweight diagnostics for SAP ArchiveLink OData connectivity.
/// </summary>
public static class SapArchiveDiagnostics
{
    /// <summary>
    /// Tests the configured SAP OData connection by fetching a CSRF token.
    /// </summary>
    public static async Task<SapUploadResult> TestConnectionAsync(SapConnectionConfig connection, CancellationToken ct = default)
    {
        var result = await new HttpSapArchiveUploader(connection).TestConnectionAsync(ct).ConfigureAwait(false);
        return new SapUploadResult(result.Success, null, null, null, null, result.ErrorMessage, null, null,
            System.Array.Empty<SapErrorDetail>());
    }
}
