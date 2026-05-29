using System;

namespace NAPS2.Sap;

/// <summary>
/// Redacts sensitive HTTP header values before they are written to logs.
/// </summary>
public static class LogValueRedactor
{
    /// <summary>
    /// Redacts Authorization header values.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The redacted header value when needed.</returns>
    public static string RedactHeader(string name, string? value)
    {
        if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase) &&
            value?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Basic ***";
        }
        return value ?? string.Empty;
    }
}
