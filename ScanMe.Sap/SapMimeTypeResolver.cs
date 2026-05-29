using System;
using System.Collections.Generic;
using System.IO;

namespace NAPS2.Sap;

/// <summary>
/// Resolves MIME types for files uploaded to the SAP ArchiveLink OData service.
/// </summary>
public static class SapMimeTypeResolver
{
    private static readonly IReadOnlyDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".gif"] = "image/gif",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".xml"] = "application/xml",
        [".zip"] = "application/zip"
    };

    /// <summary>
    /// Resolves a MIME type from a file name or extension.
    /// </summary>
    /// <param name="fileNameOrExtension">A file name or extension.</param>
    /// <returns>The resolved MIME type or <c>application/octet-stream</c>.</returns>
    public static string Resolve(string fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return "application/octet-stream";
        }
        var extension = fileNameOrExtension.StartsWith(".", StringComparison.Ordinal)
            ? fileNameOrExtension
            : Path.GetExtension(fileNameOrExtension);
        return !string.IsNullOrWhiteSpace(extension) && MimeTypes.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}
