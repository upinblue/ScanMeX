using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace NAPS2.Sap;

/// <summary>
/// Creates RFC clients for a SAP connection configuration.
/// </summary>
public interface ISapRfcClientFactory
{
    /// <summary>
    /// Creates an RFC client for the supplied connection.
    /// </summary>
    /// <param name="connection">The SAP connection configuration.</param>
    /// <returns>An RFC client.</returns>
    ISapRfcClient Create(SapConnectionConfig connection);
}

/// <summary>
/// Minimal RFC client abstraction used by ArchiveLink upload logic.
/// </summary>
public interface ISapRfcClient
{
    /// <summary>
    /// Creates a SAP RFC function by name.
    /// </summary>
    /// <param name="functionName">The RFC function name.</param>
    /// <returns>A function wrapper.</returns>
    ISapRfcFunction CreateFunction(string functionName);
}

/// <summary>
/// Minimal RFC function abstraction used by ArchiveLink upload logic.
/// </summary>
public interface ISapRfcFunction
{
    /// <summary>
    /// Sets an import/export parameter value.
    /// </summary>
    void SetValue(string name, object? value);

    /// <summary>
    /// Gets a string import/export parameter value.
    /// </summary>
    string? GetString(string name);

    /// <summary>
    /// Gets a table parameter.
    /// </summary>
    ISapRfcTable GetTable(string name);

    /// <summary>
    /// Invokes the RFC function.
    /// </summary>
    void Invoke();
}

/// <summary>
/// Minimal RFC table abstraction used by ArchiveLink upload logic.
/// </summary>
public interface ISapRfcTable
{
    /// <summary>
    /// Appends a new table row and selects it as the current row.
    /// </summary>
    void Append();

    /// <summary>
    /// Sets a field value on the current row.
    /// </summary>
    void SetValue(string name, object? value);
}

/// <summary>
/// Exception raised by the RFC abstraction. It preserves the SAP NCo exception key when available.
/// </summary>
public sealed class SapRfcException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SapRfcException" /> class.
    /// </summary>
    public SapRfcException(string? key, string message, Exception? innerException = null) : base(message, innerException)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the SAP NCo exception key when available.
    /// </summary>
    public string? Key { get; }
}

/// <summary>
/// Runtime SAP NCo implementation loaded via reflection so builds without licensed NCo packages remain possible.
/// </summary>
public sealed class NcoSapRfcClientFactory : ISapRfcClientFactory
{
    /// <inheritdoc />
    public ISapRfcClient Create(SapConnectionConfig connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        try
        {
            var connectorAssembly = LoadConnectorAssembly();
            var parameters = CreateRfcConfigParameters(connectorAssembly, connection);
            var managerType = connectorAssembly.GetType("SAP.Middleware.Connector.RfcDestinationManager", true)!;
            var destination = managerType.GetMethod("GetDestination", new[] { parameters.GetType() })!
                .Invoke(null, new[] { parameters });
            return new NcoSapRfcClient(destination!);
        }
        catch (SapRfcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ToSapRfcException(ex);
        }
    }

    private static Assembly LoadConnectorAssembly()
    {
        try
        {
            return Assembly.Load("sapnco");
        }
        catch (Exception ex)
        {
            throw new SapRfcException("SAP_NCO_NOT_FOUND",
                "SAP .NET Connector assembly 'sapnco' could not be loaded. Configure SAP NCo packages and native libraries before using RFC upload.", ex);
        }
    }

    private static object CreateRfcConfigParameters(Assembly connectorAssembly, SapConnectionConfig connection)
    {
        var type = connectorAssembly.GetType("SAP.Middleware.Connector.RfcConfigParameters", true)!;
        var parameters = Activator.CreateInstance(type)!;
        Add(parameters, GetConfigKey(type, "Name", "NAME"), BuildDestinationName(connection));
        Add(parameters, GetConfigKey(type, "AppServerHost", "ASHOST"), connection.AppServerHost ?? string.Empty);
        Add(parameters, GetConfigKey(type, "SystemNumber", "SYSNR"), connection.SystemNumber ?? string.Empty);
        Add(parameters, GetConfigKey(type, "SystemID", "SYSID"), connection.SystemId ?? string.Empty);
        Add(parameters, GetConfigKey(type, "Client", "CLIENT"), connection.Client ?? string.Empty);
        Add(parameters, GetConfigKey(type, "Language", "LANG"), string.IsNullOrWhiteSpace(connection.Language) ? "EN" : connection.Language!);
        Add(parameters, GetConfigKey(type, "User", "USER"), connection.User ?? string.Empty);

        using var securePassword = SapCredentialStore.ReadPasswordSecure(connection);
        var password = SecureStringToString(securePassword);
        try
        {
            Add(parameters, GetConfigKey(type, "Password", "PASSWD"), password);
        }
        finally
        {
            password = string.Empty;
        }

        return parameters;
    }

    private static string BuildDestinationName(SapConnectionConfig connection)
    {
        var input = string.Join("|", connection.SystemId ?? string.Empty, connection.Client ?? string.Empty, connection.User ?? string.Empty);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return $"SCANME_{connection.SystemId}_{connection.Client}_{ToHex(hash).Substring(0, 16)}";
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string GetConfigKey(Type parametersType, string fieldName, string fallback)
        => parametersType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? fallback;

    private static void Add(object parameters, string key, string value)
    {
        var add = parameters.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) });
        if (add != null)
        {
            add.Invoke(parameters, new object[] { key, value });
            return;
        }

        var indexer = parameters.GetType().GetProperty("Item", new[] { typeof(string) });
        indexer?.SetValue(parameters, value, new object[] { key });
    }

    private static string SecureStringToString(SecureString secureString)
    {
        if (secureString.Length == 0)
        {
            return string.Empty;
        }

        var bstr = IntPtr.Zero;
        try
        {
            bstr = Marshal.SecureStringToBSTR(secureString);
            return Marshal.PtrToStringBSTR(bstr) ?? string.Empty;
        }
        finally
        {
            if (bstr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(bstr);
            }
        }
    }

    internal static SapRfcException ToSapRfcException(Exception ex)
    {
        var source = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException! : ex;
        var key = source.GetType().GetProperty("Key")?.GetValue(source)?.ToString();
        return new SapRfcException(key ?? source.GetType().Name, source.Message, source);
    }

    private sealed class NcoSapRfcClient : ISapRfcClient
    {
        private readonly object _destination;

        public NcoSapRfcClient(object destination)
        {
            _destination = destination;
        }

        public ISapRfcFunction CreateFunction(string functionName)
        {
            try
            {
                var repository = _destination.GetType().GetProperty("Repository")!.GetValue(_destination)!;
                var function = repository.GetType().GetMethod("CreateFunction", new[] { typeof(string) })!
                    .Invoke(repository, new object[] { functionName });
                return new NcoSapRfcFunction(function!, _destination);
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }
    }

    private sealed class NcoSapRfcFunction : ISapRfcFunction
    {
        private readonly object _function;
        private readonly object _destination;

        public NcoSapRfcFunction(object function, object destination)
        {
            _function = function;
            _destination = destination;
        }

        public void SetValue(string name, object? value) => InvokeMethod("SetValue", name, value);

        public string? GetString(string name)
        {
            try
            {
                return _function.GetType().GetMethod("GetString", new[] { typeof(string) })!
                    .Invoke(_function, new object[] { name })?.ToString();
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }

        public ISapRfcTable GetTable(string name)
        {
            try
            {
                var table = _function.GetType().GetMethod("GetTable", new[] { typeof(string) })!
                    .Invoke(_function, new object[] { name });
                return new NcoSapRfcTable(table!);
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }

        public void Invoke()
        {
            try
            {
                _function.GetType().GetMethod("Invoke", new[] { _destination.GetType() })!
                    .Invoke(_function, new[] { _destination });
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }

        private void InvokeMethod(string name, params object?[] args)
        {
            try
            {
                var method = FindMethod(_function.GetType(), name, args.Length);
                method.Invoke(_function, args);
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }
    }

    private sealed class NcoSapRfcTable : ISapRfcTable
    {
        private readonly object _table;

        public NcoSapRfcTable(object table)
        {
            _table = table;
        }

        public void Append()
        {
            try
            {
                _table.GetType().GetMethod("Append", Type.EmptyTypes)!.Invoke(_table, null);
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }

        public void SetValue(string name, object? value)
        {
            try
            {
                FindMethod(_table.GetType(), "SetValue", 2).Invoke(_table, new[] { name, value });
            }
            catch (Exception ex)
            {
                throw ToSapRfcException(ex);
            }
        }
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount)
    {
        foreach (var method in type.GetMethods())
        {
            if (method.Name == name && method.GetParameters().Length == parameterCount)
            {
                return method;
            }
        }
        throw new MissingMethodException(type.FullName, name);
    }
}
