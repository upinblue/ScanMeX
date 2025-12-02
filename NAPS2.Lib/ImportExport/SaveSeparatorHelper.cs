using NAPS2.Scan;

namespace NAPS2.ImportExport;

internal static class SaveSeparatorHelper
{
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
            var pattern = settings.Code39SeparationPattern;
            System.Text.RegularExpressions.Regex? regex = null;
            if (!string.IsNullOrEmpty(pattern))
            {
                try { regex = new System.Text.RegularExpressions.Regex(pattern!); } catch { regex = null; }
            }

            var images = new List<ProcessedImage>();
            foreach (var scan in scans)
            {
                foreach (var image in scan)
                {
                    var barcode = image.PostProcessingData.Barcode;
                    bool isCode39Separator = barcode.IsDetected &&
                                             (regex == null || (barcode.DetectedText != null && regex.IsMatch(barcode.DetectedText)));
                    if (isCode39Separator)
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
            foreach (var group in SeparateScans(scans, settings.Separator, splitSize))
            {
                yield return group;
            }
        }
    }
}