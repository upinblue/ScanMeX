using System.Collections.Generic;

namespace NAPS2.Sap;

/// <summary>
/// Provides common SAP ArchiveLink business object types for profile configuration.
/// </summary>
public static class SapObjectTypeCatalog
{
    /// <summary>
    /// Gets a read-only list of commonly used SAP business object types.
    /// </summary>
    public static readonly IReadOnlyList<SapObjectTypeCatalogEntry> CommonTypes = new[]
    {
        new SapObjectTypeCatalogEntry("BUS2012", "Bestellung", "Einkaufsbelegnummer, meist 10-stellig"),
        new SapObjectTypeCatalogEntry("BUS2081", "Eingangsrechnung", "Rechnungsbelegnummer/Geschäftsjahr gemäß SAP-System"),
        new SapObjectTypeCatalogEntry("BUS1006", "Geschäftspartner", "Business-Partner-Nummer gemäß SAP-Customizing"),
        new SapObjectTypeCatalogEntry("EQUI", "Equipment", "Equipmentnummer, führende Nullen systemabhängig"),
        new SapObjectTypeCatalogEntry("MATERIAL", "Material", "Materialnummer, Format/Länge systemabhängig")
    };
}

/// <summary>
/// Describes one SAP business object type entry offered by <see cref="SapObjectTypeCatalog" />.
/// </summary>
public sealed class SapObjectTypeCatalogEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SapObjectTypeCatalogEntry" /> class.
    /// </summary>
    /// <param name="key">The SAP business object type key.</param>
    /// <param name="displayName">The localized display name.</param>
    /// <param name="keyFormatHint">A short hint describing the expected object key format.</param>
    public SapObjectTypeCatalogEntry(string key, string displayName, string keyFormatHint)
    {
        Key = key;
        DisplayName = displayName;
        KeyFormatHint = keyFormatHint;
    }

    /// <summary>
    /// Gets the SAP business object type key, for example <c>BUS2012</c>.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the human-readable display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a short hint describing the expected object key format.
    /// </summary>
    public string KeyFormatHint { get; }
}
