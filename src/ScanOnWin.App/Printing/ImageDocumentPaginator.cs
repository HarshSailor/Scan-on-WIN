using System.Printing;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScanPage = ScanOnWin.Core.DocumentPage;

namespace ScanOnWin.App.Printing;

public readonly record struct PrintLayout(Size PageSize, Rect ImageableArea)
{
    public static PrintLayout A4 => new(new Size(793.7, 1122.5), new Rect(24, 24, 745.7, 1074.5));

    public static PrintLayout From(PrintTicket ticket, PrintCapabilities? capabilities)
    {
        var medium = ticket.PageMediaSize;
        var supported = medium?.PageMediaSizeName == null ? null : capabilities?.PageMediaSizeCapability
            .FirstOrDefault(size => size.PageMediaSizeName == medium.PageMediaSizeName);
        var width = medium?.Width ?? supported?.Width ?? A4.PageSize.Width;
        var height = medium?.Height ?? supported?.Height ?? A4.PageSize.Height;
        if (ticket.PageOrientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape)
            (width, height) = (height, width);
        // PageMediaSize is the full sheet. Capabilities' oriented dimensions can describe
        // the printable area; using them as the sheet would trim the hardware margins twice.
        if (!Valid(width) || !Valid(height)) return A4;
        var bounds = new Rect(0, 0, width, height);
        var area = capabilities?.PageImageableArea;
        var imageable = area == null ? new Rect(24, 24, Math.Max(1, width - 48), Math.Max(1, height - 48))
            : new Rect(Math.Max(0, area.OriginWidth), Math.Max(0, area.OriginHeight), Math.Max(0, area.ExtentWidth), Math.Max(0, area.ExtentHeight));
        imageable.Intersect(bounds);
        if (imageable.IsEmpty || imageable.Width < 1 || imageable.Height < 1) throw new InvalidOperationException("Драйвер принтера не сообщил доступную область печати. Проверьте размер бумаги в свойствах принтера.");
        return new(bounds.Size, imageable);
    }

    private static bool Valid(double value) => double.IsFinite(value) && value > 0;
}

/// <summary>Renders application working images directly, without creating an export or changing the document.</summary>
public sealed class ImageDocumentPaginator : DocumentPaginator
{
    private readonly string[] _paths;
    private readonly PrintLayout _layout;
    public ImageDocumentPaginator(IReadOnlyList<ScanPage> pages, IReadOnlyList<int> pageNumbers, PrintLayout layout)
    {
        if (pageNumbers.Count == 0) throw new ArgumentException("Не выбраны страницы для печати.", nameof(pageNumbers));
        _paths = pageNumbers.Select(number => number >= 0 && number < pages.Count ? pages[number].CurrentPath : throw new ArgumentOutOfRangeException(nameof(pageNumbers))).ToArray();
        _layout = layout;
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _paths.Length;
    public override Size PageSize { get => _layout.PageSize; set { /* The validated printer layout defines the media. */ } }
    public override IDocumentPaginatorSource? Source => null;

    public override System.Windows.Documents.DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _paths.Length) return System.Windows.Documents.DocumentPage.Missing;
        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(_paths[pageNumber]))
        {
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        }
        var destination = Fit(new Size(bitmap.PixelWidth, bitmap.PixelHeight), _layout.ImageableArea);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(_layout.PageSize));
            drawing.PushClip(new RectangleGeometry(_layout.ImageableArea));
            drawing.DrawImage(bitmap, destination);
            drawing.Pop();
        }
        return new System.Windows.Documents.DocumentPage(visual, _layout.PageSize, new Rect(_layout.PageSize), _layout.ImageableArea);
    }

    public static Rect Fit(Size pixels, Rect area)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || area.Width <= 0 || area.Height <= 0) throw new ArgumentOutOfRangeException(nameof(pixels));
        var scale = Math.Min(area.Width / pixels.Width, area.Height / pixels.Height);
        var width = pixels.Width * scale; var height = pixels.Height * scale;
        return new Rect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }
}
