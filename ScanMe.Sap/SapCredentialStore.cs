using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace NAPS2.Sap;

/// <summary>
/// Protects and unprotects SAP passwords using Windows DPAPI in current-user scope.
/// </summary>
public static class SapCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ScanMe.Sap.ArchiveLink");

    /// <summary>
    /// Encrypts a plain text password for storage in <see cref="SapConnectionConfig.EncryptedPassword" />.
    /// </summary>
    /// <param name="plainTextPassword">The plain text password. It is never returned or persisted by this method.</param>
    /// <returns>A base64-encoded DPAPI payload, or an empty string when the input is empty.</returns>
    public static string ProtectPassword(string? plainTextPassword)
    {
        if (string.IsNullOrEmpty(plainTextPassword))
        {
            return string.Empty;
        }

        var data = Encoding.UTF8.GetBytes(plainTextPassword);
        var protectedData = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    /// <summary>
    /// Decrypts a password stored in <see cref="SapConnectionConfig.EncryptedPassword" />.
    /// </summary>
    /// <param name="encryptedPassword">The base64-encoded DPAPI payload.</param>
    /// <returns>The decrypted plain text password, or an empty string when no password is stored.</returns>
    public static string UnprotectPassword(string? encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
        {
            return string.Empty;
        }

        var protectedData = Convert.FromBase64String(encryptedPassword);
        var data = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    /// <summary>
    /// Stores an encrypted password on the provided SAP connection configuration.
    /// </summary>
    /// <param name="config">The SAP connection configuration to update.</param>
    /// <param name="plainTextPassword">The plain text password to protect.</param>
    public static void WritePassword(SapConnectionConfig config, string? plainTextPassword)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        config.EncryptedPassword = ProtectPassword(plainTextPassword);
    }

    /// <summary>
    /// Reads and decrypts the password stored on the provided SAP connection configuration.
    /// </summary>
    /// <param name="config">The SAP connection configuration to read.</param>
    /// <returns>The decrypted plain text password, or an empty string when no password is stored.</returns>
    public static string ReadPassword(SapConnectionConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        return UnprotectPassword(config.EncryptedPassword);
    }

    /// <summary>
    /// Decrypts a password into a <see cref="SecureString" /> for short-lived RFC configuration use.
    /// </summary>
    /// <param name="encryptedPassword">The base64-encoded DPAPI payload.</param>
    /// <returns>A read-only secure string containing the decrypted password.</returns>
    public static SecureString UnprotectPasswordSecure(string? encryptedPassword)
    {
        var secureString = new SecureString();
        if (string.IsNullOrEmpty(encryptedPassword))
        {
            secureString.MakeReadOnly();
            return secureString;
        }

        var protectedData = Convert.FromBase64String(encryptedPassword);
        var data = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
        var chars = Encoding.UTF8.GetChars(data);
        try
        {
            foreach (var c in chars)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
        finally
        {
            Array.Clear(data, 0, data.Length);
            Array.Clear(chars, 0, chars.Length);
        }
    }

    /// <summary>
    /// Reads and decrypts the password stored on the provided SAP connection configuration into a <see cref="SecureString" />.
    /// </summary>
    /// <param name="config">The SAP connection configuration to read.</param>
    /// <returns>A read-only secure string containing the decrypted password.</returns>
    public static SecureString ReadPasswordSecure(SapConnectionConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        return UnprotectPasswordSecure(config.EncryptedPassword);
    }
}
