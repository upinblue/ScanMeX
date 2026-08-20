#nullable enable
using NAPS2.ImportExport;
using NAPS2.Scan;
using NAPS2.Serialization;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Restricting barcode detection to part of the page. Both directions are silent on the finished
/// document: an area that is ignored leaves the phantom barcodes it was drawn to keep out, and an area
/// that is applied when nobody asked for one makes a real barcode disappear with nothing to say why. So
/// what a profile without the setting reads as is pinned here alongside what one with it does.
/// </summary>
public class BarcodeSearchAreaTests
{
    /// <summary>
    /// The one that matters most: a profile written before the setting existed has neither element in its
    /// file, and it has to go on searching the whole page.
    /// </summary>
    [Fact]
    public void AProfileSavedBeforeTheSearchAreaExistedSearchesTheWholePage()
    {
        var stored = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39]
            }
        };
        var workflow = DocumentWorkflowSettings.ForProfile(stored);

        Assert.False(workflow.RestrictBarcodeArea);
        Assert.Null(workflow.BarcodeArea);
        Assert.Null(workflow.GetBarcodeSearchArea());
        Assert.Null(BarcodeDetectionPlan.For(stored).SearchArea);

        // And for a profile with no workflow block at all, where the legacy auto save settings are read.
        var legacy = new ScanProfile
        {
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings
            {
                FilePath = @"C:\Scans\$(barcode).pdf",
                Separator = SaveSeparator.Code39Barcode
            }
        };

        Assert.Null(DocumentWorkflowSettings.ForProfile(legacy).GetBarcodeSearchArea());
        Assert.Null(BarcodeDetectionPlan.For(legacy).SearchArea);
    }

    /// <summary>
    /// A profile file written before the setting existed doesn't merely lack a value for it -- the
    /// elements aren't in the XML at all, which is the case the deserializer has to leave alone.
    /// </summary>
    [Fact]
    public void AStoredProfileWithoutTheElementsDeserializesToTheWholePage()
    {
        var xml = """
                  <DocumentWorkflowSettings>
                    <Version>1</Version>
                    <SeparationMode>Barcode</SeparationMode>
                    <BarcodeStrictness>Strict</BarcodeStrictness>
                  </DocumentWorkflowSettings>
                  """;

        var workflow = xml.FromXml<DocumentWorkflowSettings>();

        Assert.False(workflow.RestrictBarcodeArea);
        Assert.Null(workflow.BarcodeArea);
        Assert.Null(workflow.GetBarcodeSearchArea());
    }

    /// <summary>
    /// The area has to survive being written and read back, or the operator draws it, scans, and gets the
    /// whole page searched with nothing to say why.
    /// </summary>
    [Fact]
    public void ADrawnAreaSurvivesReloading()
    {
        var workflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            RestrictBarcodeArea = true,
            BarcodeArea = new BarcodeSearchArea { X = 0.1, Y = 0.2, Width = 0.5, Height = 0.25 }
        };

        var reloaded = workflow.ToXml().FromXml<DocumentWorkflowSettings>();
        var area = reloaded.GetBarcodeSearchArea();

        Assert.NotNull(area);
        Assert.Equal(0.1, area!.X, 6);
        Assert.Equal(0.2, area.Y, 6);
        Assert.Equal(0.5, area.Width, 6);
        Assert.Equal(0.25, area.Height, 6);
    }

    /// <summary>
    /// The area is kept while the box is off so that turning the restriction off and on again doesn't
    /// lose the rectangle -- but nothing may read it in the meantime.
    /// </summary>
    [Fact]
    public void AnAreaIsIgnoredWhileTheRestrictionIsOff()
    {
        var workflow = new DocumentWorkflowSettings
        {
            RestrictBarcodeArea = false,
            BarcodeArea = new BarcodeSearchArea { X = 0.1, Y = 0.2, Width = 0.5, Height = 0.25 }
        };

        Assert.Null(workflow.GetBarcodeSearchArea());
        Assert.NotNull(workflow.BarcodeArea);
    }

    /// <summary>
    /// "Restricted to the whole page" is the same instruction as "not restricted", and saying so here is
    /// what keeps the detector from copying a full page to hand it back to itself.
    /// </summary>
    [Fact]
    public void AnAreaCoveringTheWholePageIsNotARestriction()
    {
        var workflow = new DocumentWorkflowSettings
        {
            RestrictBarcodeArea = true,
            BarcodeArea = BarcodeSearchArea.WholePage
        };

        Assert.Null(workflow.GetBarcodeSearchArea());
    }

    /// <summary>
    /// A stored area of no size would mean "decode nothing", which is the silent failure this whole
    /// feature has to avoid rather than cause. It can only come from a hand-edited profile, so it is not
    /// worth honouring -- the whole page is searched instead.
    /// </summary>
    [Fact]
    public void AnAreaThatHasCollapsedToNothingSearchesTheWholePage()
    {
        var workflow = new DocumentWorkflowSettings
        {
            RestrictBarcodeArea = true,
            BarcodeArea = new BarcodeSearchArea { X = 0.5, Y = 0.5, Width = 0, Height = 0 }
        };

        Assert.Null(workflow.GetBarcodeSearchArea());
    }

    [Fact]
    public void TheAreaIsHandedToTheDetector()
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39],
                RestrictBarcodeArea = true,
                BarcodeArea = new BarcodeSearchArea { X = 0, Y = 0, Width = 1, Height = 0.25 }
            }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.True(plan.Detect);
        Assert.NotNull(plan.SearchArea);
        Assert.Equal(0.25, plan.SearchArea!.Height, 6);
    }

    /// <summary>
    /// Patch-T separation goes down the legacy path, which is a decoding path like any other: the mark on
    /// a separator card is in a fixed place too, so the restriction has to reach it.
    /// </summary>
    [Fact]
    public void TheAreaAlsoReachesThePatchTPath()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { Separator = SaveSeparator.PatchT },
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                RestrictBarcodeArea = true,
                BarcodeArea = new BarcodeSearchArea { X = 0, Y = 0.75, Width = 1, Height = 0.25 }
            }
        };

        var plan = BarcodeDetectionPlan.For(profile, detectPatchT: true);

        Assert.True(plan.Detect);
        Assert.NotNull(plan.SearchArea);
        Assert.Equal(0.75, plan.SearchArea!.Y, 6);
    }

    /// <summary>
    /// The values come out of a file a person can edit, so every consumer normalizes rather than trusting
    /// them. An area reaching past the edge of the page is brought back inside it rather than producing a
    /// crop rectangle that doesn't fit the scan.
    /// </summary>
    [Theory]
    [InlineData(-0.5, -0.5, 2, 2, 0, 0, 1, 1)]
    [InlineData(0.9, 0.9, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5)]
    [InlineData(0.2, 0.3, 0.001, 0.001, 0.2, 0.3, 0.05, 0.05)]
    public void AnAreaIsBroughtBackInsideThePage(double x, double y, double width, double height,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var area = new BarcodeSearchArea { X = x, Y = y, Width = width, Height = height }.Normalized();

        Assert.Equal(expectedX, area.X, 6);
        Assert.Equal(expectedY, area.Y, 6);
        Assert.Equal(expectedWidth, area.Width, 6);
        Assert.Equal(expectedHeight, area.Height, 6);
    }

    /// <summary>
    /// The edges are rounded outwards: a barcode printed right at the boundary of the area drawn would
    /// otherwise be cut in half by a rounding error, and half a barcode does not decode.
    /// </summary>
    [Fact]
    public void ThePixelRectangleCoversTheAreaAndFitsTheImage()
    {
        var area = new BarcodeSearchArea { X = 0.1, Y = 0.25, Width = 0.333, Height = 0.5 };

        var (x, y, width, height) = area.ToPixels(2480, 3508);

        Assert.Equal(248, x);
        Assert.Equal(877, y);
        Assert.Equal(826, width);
        Assert.Equal(1754, height);
        Assert.True(x + width <= 2480);
        Assert.True(y + height <= 3508);
    }

    /// <summary>
    /// An area right at the bottom-right corner is where rounding outwards would push the rectangle off
    /// the image, which is a crop that throws rather than a barcode that isn't found.
    /// </summary>
    [Fact]
    public void ThePixelRectangleNeverLeavesTheImage()
    {
        var area = new BarcodeSearchArea { X = 0.95, Y = 0.95, Width = 0.05, Height = 0.05 };

        var (x, y, width, height) = area.ToPixels(101, 101);

        Assert.True(x >= 0 && y >= 0);
        Assert.True(width >= 1 && height >= 1);
        Assert.True(x + width <= 101);
        Assert.True(y + height <= 101);
    }
}
