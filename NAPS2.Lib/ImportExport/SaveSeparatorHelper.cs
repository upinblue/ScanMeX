using NAPS2.Scan;
using NLog;
using System.Text.RegularExpressions;

namespace NAPS2.ImportExport;

internal static class SaveSeparatorHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Given a list of scans (each of which is a list of 1 or more images),
    /// split up the images into multiple lists as described by the SaveSeparator parameter.
    /// </summary>
    /// <param name="scans"></param>
    /// <param name="separator"></param>
    /// <param name="splitSize"></param>
    /// <returns></returns>
    public static IEnumerable<List<ProcessedImage>> SeparateScans(IEnumerable<IEnumerable<ProcessedImage>> scans, SaveSeparator separator, int splitSize = 1)
    {
        if (separator == SaveSeparator.FilePerScan)
        {
            foreach (var scan in scans)
            {
                var images = scan.ToList();
                if (images.Count > 0)
                {
                    yield return images;
                }
            }
        }
        else if (separator == SaveSeparator.FilePerPage)
        {
            splitSize = Math.Max(splitSize, 1);
            foreach (var scan in scans.Select(x => x.ToList()))
            {
                for (int i = 0; i < scan.Count; i += splitSize)
                {
                    yield return scan.Skip(i).Take(splitSize).ToList();
                }
            }
        }
        else if (separator == SaveSeparator.PatchT)
        {
            var images = new List<ProcessedImage>();
            foreach (var scan in scans)
            {
                foreach (var image in scan)
                {
                    if (image.PostProcessingData.Barcode.IsPatchT)
                    {
                        image.Dispose();
                        if (images.Count > 0)
                        {
                            yield return images;
                            images = [];
                        }
                    }
                    else
                    {
                        images.Add(image);
                    }
                }
            }
            if (images.Count > 0)
            {
                yield return images;
            }
        }
        else
        {
            var images = scans.SelectMany(x => x.ToList()).ToList();
            if (images.Count > 0)
            {
                yield return images;
            }
        }
    }

    /// <summary>
    /// Extended separation that can use AutoSaveSettings, including Code39 regex-based separation.
    /// </summary>
    public static IEnumerable<List<ProcessedImage>> SeparateScans(IEnumerable<IEnumerable<ProcessedImage>> scans, AutoSaveSettings settings, int splitSize = 1)
    {
        if (settings.Separator == SaveSeparator.Code39Barcode)
        {
            Regex? regex = null;
            var pattern = settings.Code39SeparationPattern?.Trim();
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                try
                {
                    regex = new Regex(pattern, RegexOptions.CultureInvariant);
                }
                catch
                {
                    regex = null;
                }
            }

            var currentDocument = new List<ProcessedImage>();
            int pageIndex = 0;

            foreach (var scan in scans)
            {
                foreach (var image in scan)
                {
                    pageIndex++;
                    var barcode = image.PostProcessingData.Barcode;
                    var text = barcode.DetectedText;

                    // Some scan/import paths may not populate barcode format metadata yet.
                    // In Code39 separation mode, fall back to detected text when format is unknown.
                    bool hasDetectedText = barcode.IsDetected && !string.IsNullOrWhiteSpace(text);
                    bool hasCode39 = hasDetectedText && (barcode.IsCode39 || string.IsNullOrWhiteSpace(barcode.DetectedFormat));
                    bool isSeparator = hasCode39 && (regex == null || regex.IsMatch(text!));

                    if (isSeparator)
                    {
                        _logger.Debug($"Code39 separator detected on page {pageIndex} text='{text}' format='{barcode.DetectedFormat ?? "<unknown>"}' regex='{pattern ?? "<none>"}'");

                        // Barcode page defines the start of a new document.
                        if (currentDocument.Count > 0)
                        {
                            yield return currentDocument;
                            currentDocument = [];
                        }

                        currentDocument.Add(image);
                    }
                    else
                    {
                        if (hasDetectedText)
                        {
                            _logger.Debug($"Barcode detected but not Code39 separator on page {pageIndex} text='{text}' format='{barcode.DetectedFormat ?? "<unknown>"}' regex='{pattern ?? "<none>"}'");
                        }
                        currentDocument.Add(image);
                    }
                }
            }

            if (currentDocument.Count > 0)
            {
                yield return currentDocument;
            }
        }
        else
        {
            foreach (var group in SeparateScans(scans, settings.Separator, splitSize))
            {
                yield return group;
            }
        }
    }
}