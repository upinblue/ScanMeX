namespace NAPS2.Scan;

/// <summary>
/// The part of a page barcodes are looked for in, as fractions of the page's width and height rather
/// than as pixels, so one setting holds for every resolution and paper size the profile is ever scanned
/// at.
/// </summary>
/// <remarks>
/// Null everywhere it appears means "the whole page", which is what every profile written before this
/// existed deserializes to. Restricting the search is worth doing for the same reason detection is
/// refused without a symbology restriction: the ruled tables
/// and dense print of a real form decode as barcodes that are not on the paper, and a phantom that names
/// a file or an archive key is indistinguishable from a correct scan afterwards. Paperwork that always
/// carries its barcode in the same place can say so here and have the rest of the sheet ignored.
///
/// The cost of getting it wrong is the opposite failure -- a barcode that is on the paper but outside
/// the area is never seen -- so the area is stated in the console on every scan it is in force on, and
/// an area that has collapsed to nothing falls back to the whole page rather than silently decoding
/// nothing.
/// </remarks>
public record BarcodeSearchArea
{
    /// <summary>
    /// The whole page, which is what a profile that never restricted the search gets.
    /// </summary>
    public static readonly BarcodeSearchArea WholePage = new();

    /// <summary>
    /// The top quarter of the page, where a cover sheet's order barcode usually sits.
    /// </summary>
    public static readonly BarcodeSearchArea TopHeader = new() { Y = 0, Height = 0.25 };

    /// <summary>
    /// The bottom quarter of the page.
    /// </summary>
    public static readonly BarcodeSearchArea BottomFooter = new() { Y = 0.75, Height = 0.25 };

    /// <summary>
    /// The smallest area that can be selected, as a fraction of the page. A band much narrower than this
    /// on a 300 dpi A4 page is a few millimetres of paper, which is smaller than the quiet zone a barcode
    /// needs on either side -- an area that tight reads as "detection is broken" rather than as a
    /// restriction.
    /// </summary>
    public const double MIN_SIZE = 0.05;

    /// <summary>The left edge, 0 (the left of the page) to 1.</summary>
    public double X { get; init; }

    /// <summary>The top edge, 0 (the top of the page) to 1.</summary>
    public double Y { get; init; }

    /// <summary>The width as a fraction of the page's width. Defaults to the whole width.</summary>
    public double Width { get; init; } = 1;

    /// <summary>The height as a fraction of the page's height. Defaults to the whole height.</summary>
    public double Height { get; init; } = 1;

    /// <summary>
    /// Whether the area covers the whole page, in which case cropping to it would be work with no effect.
    /// </summary>
    public bool IsWholePage => X <= 0 && Y <= 0 && X + Width >= 1 && Y + Height >= 1;

    /// <summary>
    /// Whether the area is big enough to look for a barcode in at all. A stored area of zero size -- a
    /// hand-edited profile, or one written by a future version -- must not turn into "decode nothing".
    /// </summary>
    public bool IsUsable => Width > 0 && Height > 0 && X < 1 && Y < 1 && X + Width > 0 && Y + Height > 0;

    /// <summary>
    /// The same area with its edges brought back inside the page and its size brought up to
    /// <see cref="MIN_SIZE"/>. Every consumer normalizes rather than trusting the stored values, because
    /// these come out of a profile file that a person can edit.
    /// </summary>
    /// <remarks>
    /// Clamping goes through <see cref="NumberExtensions.Clamp{T}"/> rather than <c>Math.Clamp</c>:
    /// NAPS2.Sdk still targets net462, where <c>Math.Clamp</c> does not exist, so the house extension is
    /// the one that compiles on every target framework.
    /// </remarks>
    public BarcodeSearchArea Normalized()
    {
        var width = Width.Clamp(MIN_SIZE, 1d);
        var height = Height.Clamp(MIN_SIZE, 1d);
        return new BarcodeSearchArea
        {
            X = X.Clamp(0d, 1 - width),
            Y = Y.Clamp(0d, 1 - height),
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// The area in pixels of an image of the given size, clamped so it always names at least one pixel
    /// inside the image.
    /// </summary>
    /// <remarks>
    /// The edges are rounded outwards. A barcode printed right at the boundary of the area the operator
    /// drew would otherwise be cut in half by a rounding error, and half a barcode does not decode.
    /// </remarks>
    public (int X, int Y, int Width, int Height) ToPixels(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return (0, 0, Math.Max(imageWidth, 0), Math.Max(imageHeight, 0));
        }
        var area = Normalized();
        var left = ((int) Math.Floor(area.X * imageWidth)).Clamp(0, imageWidth - 1);
        var top = ((int) Math.Floor(area.Y * imageHeight)).Clamp(0, imageHeight - 1);
        var width = ((int) Math.Ceiling(area.Width * imageWidth)).Clamp(1, imageWidth - left);
        var height = ((int) Math.Ceiling(area.Height * imageHeight)).Clamp(1, imageHeight - top);
        return (left, top, width, height);
    }

    /// <summary>
    /// The area as whole percentages, for the console and for the profile dialog's readout.
    /// </summary>
    public (int X, int Y, int Width, int Height) ToPercent()
    {
        var area = Normalized();
        return ((int) Math.Round(area.X * 100), (int) Math.Round(area.Y * 100),
            (int) Math.Round(area.Width * 100), (int) Math.Round(area.Height * 100));
    }

    public override string ToString()
    {
        var (x, y, width, height) = ToPercent();
        return $"x={x}%, y={y}%, w={width}%, h={height}%";
    }
}
