using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NAPS2.Sap;

/// <summary>
/// Specifies where the SAP ArchiveLink object key should be obtained from.
/// </summary>
public enum ObjectKeySource
{
    /// <summary>
    /// Ask the user for the object key before uploading.
    /// </summary>
    PromptUser,

    /// <summary>
    /// Extract the object key from a barcode value.
    /// </summary>
    FromBarcode,

    /// <summary>
    /// Extract the object key from the saved file name.
    /// </summary>
    FromFilename,

    /// <summary>
    /// Use a fixed configured object key.
    /// </summary>
    Fixed
}

/// <summary>
/// Contains SAP ArchiveLink upload settings embedded in a ScanMe scan profile.
/// </summary>
public class SapArchiveProfileSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether SAP ArchiveLink upload is enabled for this profile.
    /// </summary>
    public bool EnableUpload { get; set; }

    /// <summary>
    /// Gets or sets the SAP content repository/archive ID, for example <c>A1</c>.
    /// </summary>
    public string? ArchiveId { get; set; }

    /// <summary>
    /// Gets or sets the SAP business object type, for example <c>BUS2012</c>.
    /// </summary>
    public string? SapObjectType { get; set; }

    /// <summary>
    /// Gets or sets the ArchiveLink document type, for example <c>ZSCAN_PDF</c>.
    /// </summary>
    public string? ArDocType { get; set; }

    /// <summary>
    /// Gets or sets the source used to determine the SAP object key.
    /// </summary>
    public ObjectKeySource ObjectKeySource { get; set; } = ObjectKeySource.PromptUser;

    /// <summary>
    /// Gets or sets the fixed object key used when <see cref="ObjectKeySource" /> is <see cref="ObjectKeySource.Fixed" />.
    /// </summary>
    public string? FixedObjectKey { get; set; }

    /// <summary>
    /// Gets or sets the regular expression used to extract an object key from barcode values.
    /// </summary>
    public string? BarcodeRegex { get; set; }

    /// <summary>
    /// Gets or sets the regular expression used to extract an object key from the saved file name.
    /// </summary>
    public string? FilenameRegex { get; set; }

    /// <summary>
    /// Gets or sets the optional SAP ArchiveLink description template.
    /// </summary>
    public string? DescriptionTemplate { get; set; }

    /// <summary>
    /// Validates the profile settings for configuration completeness and consistency.
    /// </summary>
    /// <returns>A validation result containing all detected configuration errors.</returns>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (!EnableUpload)
        {
            return ValidationResult.Success;
        }

        if (string.IsNullOrWhiteSpace(ArchiveId))
        {
            errors.Add("ArchiveId is required when SAP ArchiveLink upload is enabled.");
        }
        if (string.IsNullOrWhiteSpace(SapObjectType))
        {
            errors.Add("SapObjectType is required when SAP ArchiveLink upload is enabled.");
        }
        if (string.IsNullOrWhiteSpace(ArDocType))
        {
            errors.Add("ArDocType is required when SAP ArchiveLink upload is enabled.");
        }

        switch (ObjectKeySource)
        {
            case ObjectKeySource.Fixed:
                if (string.IsNullOrWhiteSpace(FixedObjectKey))
                {
                    errors.Add("FixedObjectKey is required when ObjectKeySource is Fixed.");
                }
                break;
            case ObjectKeySource.FromBarcode:
                if (string.IsNullOrWhiteSpace(BarcodeRegex))
                {
                    errors.Add("BarcodeRegex is required when ObjectKeySource is FromBarcode.");
                }
                else
                {
                    AddRegexError(errors, BarcodeRegex, "BarcodeRegex");
                }
                break;
            case ObjectKeySource.FromFilename:
                if (string.IsNullOrWhiteSpace(FilenameRegex))
                {
                    errors.Add("FilenameRegex is required when ObjectKeySource is FromFilename.");
                }
                else
                {
                    AddRegexError(errors, FilenameRegex, "FilenameRegex");
                }
                break;
        }

        return errors.Count == 0 ? ValidationResult.Success : ValidationResult.Failure(errors);
    }

    private static void AddRegexError(List<string> errors, string pattern, string fieldName)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{fieldName} is invalid: {ex.Message}");
        }
    }
}

/// <summary>
/// Represents the result of validating a configuration object.
/// </summary>
public sealed class ValidationResult
{
    private static readonly ValidationResult ValidResult = new ValidationResult(Array.Empty<string>());

    private ValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    /// <summary>
    /// Gets a successful validation result.
    /// </summary>
    public static ValidationResult Success => ValidResult;

    /// <summary>
    /// Gets a value indicating whether validation completed without errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Creates a failed validation result from a set of error messages.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    /// <returns>A failed validation result.</returns>
    public static ValidationResult Failure(IReadOnlyList<string> errors)
    {
        if (errors == null)
        {
            throw new ArgumentNullException(nameof(errors));
        }
        return new ValidationResult(errors);
    }
}
