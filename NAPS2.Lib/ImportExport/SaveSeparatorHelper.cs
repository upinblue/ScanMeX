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

    public static IEnumerable<List<ProcessedImage>> SegmentByPatchTKeepingSeparator(IEnumerable<ProcessedImage> source)
    {
        var current = new List<ProcessedImage>();
        foreach (var image in source)
        {
            if (image.PostProcessingData.Barcode.IsPatchT && current.Count > 0)
            {
                yield return current;
                current = [];
            }
            // Keep the separator sheet: it carries the visual/business barcode used by SAP ArchiveLink.
            current.Add(image);
        }
        if (current.Count > 0)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Extended separation for a specific profile. Barcode-driven separation is delegated to
    /// <see cref="DocumentSeparator"/> so document boundaries are decided in exactly one place.
    /// </summary>
    public static IEnumerable<List<ProcessedImage>> SeparateScans(
        IEnumerable<IEnumerable<ProcessedImage>> scans, ScanProfile? profile, AutoSaveSettings settings,
        int splitSize = 1)
    {
        var workflow = profile?.DocumentWorkflow ?? DocumentWorkflowSettings.ForProfile(
            new ScanProfile { AutoSaveSettings = settings });
        if (workflow.SeparationMode == DocumentSeparationMode.None)
        {
            return SeparateScans(scans, settings.Separator, splitSize);
        }
        return DocumentSeparator.Separate(scans.SelectMany(x => x), workflow)
            .Select(x => x.Images.ToList());
    }
}