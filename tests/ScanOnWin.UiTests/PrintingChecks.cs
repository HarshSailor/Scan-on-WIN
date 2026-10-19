using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using ScanOnWin.App.Printing;
using ScanOnWin.Core;

/// <summary>Software checks only: never opens a physical queue or submits a printer job.</summary>
internal static class PrintingChecks
{
    public static void Run(string root)
    {
        var source = new DocumentPrintSource(false);
        source.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        source.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(source.ScannerName == "Samsung SCX-4200 Series" && source.ScannerId == "scanner-one", "Repeated successful pages retain document scanner source");
        source.PageAdded("twain:two", "Other MFP", "scanner-two");
        Check(source.ScannerName == null && source.ScannerId == null, "Mixed scanner sources require explicit printer selection");
        source.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(source.ScannerName == null && source.ScannerId == null, "Mixed document cannot silently become single-source again");
        source.Reset(); source.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(source.ScannerId == "scanner-one", "New document reset restores source tracking");
        var recoveredSource = new DocumentPrintSource(true);
        recoveredSource.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(recoveredSource.ScannerName == null && recoveredSource.ScannerId == null, "Recovered pages keep acquisition source unknown");
        recoveredSource.Reset(); recoveredSource.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(recoveredSource.ScannerName == "Samsung SCX-4200 Series", "Recovered document reset restores source tracking");
        source.MarkUnknown(); source.PageAdded("wia:one", "Samsung SCX-4200 Series", "scanner-one");
        Check(source.ScannerName == null, "Explicit unknown origin remains unknown until reset");
        Check(PageSelection.Parse("4, 1, 3–4; 1", 5).SequenceEqual(new[] { 0, 2, 3 }), "Print pages are deduplicated in document order");
        foreach (var invalid in new[] { "", "0", "6", "3-1", "1,", "-1", "1-2-3", "99999999999999", "1.5" })
        {
            bool rejected = false;
            try { PageSelection.Parse(invalid, 5); } catch (FormatException) { rejected = true; }
            Check(rejected, "Invalid print range must be rejected: " + invalid);
        }
        var samsung = new PrinterDescriptor("Samsung SCX-4200 Series", "Samsung SCX-4200 Series", "USB001");
        var other = new PrinterDescriptor("Microsoft Print to PDF", "Microsoft Print To PDF");
        Check(PrinterMatcher.Match("Samsung SCX-4200 (WIA)", null, new[] { other, samsung }).Printer == samsung, "Unique MFP model match");
        var actualSamsung = new PrinterDescriptor("Samsung SCX-4200 Series (USB001)", "Samsung Universal Print Driver 3", "USB001");
        Check(PrinterMatcher.Match("Samsung SCX-4200 Series", null, new[] { other, actualSamsung }).Printer == actualSamsung, "Installed MFP port decoration with universal driver");
        var duplicate = new PrinterDescriptor("Samsung copy", "Samsung SCX-4200 Series", "USB002");
        var ambiguous = PrinterMatcher.Match("Samsung SCX-4200", null, new[] { samsung, duplicate });
        Check(ambiguous.Printer == null && ambiguous.IsAmbiguous, "Ambiguous model must require manual printer choice");
        Check(PrinterMatcher.Match("Samsung SCX-4200", null, new[] { other }).Printer == null, "Unrelated printer must not be proposed");
        Check(PrinterMatcher.Match("Samsung", null, new[] { samsung }).Printer == null, "Manufacturer alone cannot match a printer");
        var network = new PrinterDescriptor("Office MFP", "Generic", "IP_192.168.1.40");
        Check(PrinterMatcher.Match("Scanner", "http://192.168.1.40/eSCL", new[] { network, samsung }).Printer == network, "Unique explicit IP identity match");
        Check(PrinterMatcher.Match("Scanner", "http://192.168.1.40/eSCL", new[] { network, network with { Name = "Office second queue" } }).IsAmbiguous, "Shared identity remains ambiguous");
        var wrongAddress = samsung with { PortName = "IP_192.168.1.41" };
        Check(PrinterMatcher.Match("Samsung SCX-4200", "http://192.168.1.40/eSCL", new[] { wrongAddress }).Printer == null, "Conflicting host must defeat matching model name");
        var wrongSerial = samsung with { Comment = "serial:B12345" };
        Check(PrinterMatcher.Match("Samsung SCX-4200", "serial:A12345", new[] { wrongSerial }).Printer == null, "Conflicting serial must defeat matching model name");
        var hostButWrongSerial = network with { Comment = "serial:B12345" };
        Check(PrinterMatcher.Match("Samsung SCX-4200", "http://192.168.1.40/eSCL serial:A12345", new[] { hostButWrongSerial }).Printer == null, "Matching host cannot override a conflicting explicit serial");

        var area = new Rect(20, 30, 560, 740);
        var fit = ImageDocumentPaginator.Fit(new Size(600, 1200), area);
        Check(Near(fit.Width, 370) && Near(fit.Height, 740) && Near(fit.Left, 115) && Near(fit.Top, 30), "Portrait fit preserves aspect and imageable margins");
        var landscape = ImageDocumentPaginator.Fit(new Size(1200, 600), area);
        Check(Near(landscape.Width, 560) && Near(landscape.Height, 280) && Near(landscape.Top, 260), "Landscape image is centered and not stretched");
        var layout = new PrintLayout(new Size(600, 800), area);
        var folder = Path.Combine(root, "printing"); Directory.CreateDirectory(folder);
        var red = Path.Combine(folder, "red.png"); var blue = Path.Combine(folder, "blue.png"); var green = Path.Combine(folder, "green.png");
        Image(red, Colors.Red); Image(blue, Colors.Blue); Image(green, Colors.Green);
        var pages = new[] { new DocumentPage { CurrentPath = red }, new DocumentPage { CurrentPath = blue }, new DocumentPage { CurrentPath = green } };
        var paginator = new ImageDocumentPaginator(pages, PageSelection.Parse("1,3", 3), layout);
        Check(paginator.PageCount == 2 && paginator.IsPageCountValid, "Selected subset produces exactly two printable pages");
        Check(paginator.GetPage(2) == System.Windows.Documents.DocumentPage.Missing, "Out of bounds paginator page");
        for (int i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            var target = new RenderTargetBitmap(600, 800, 96, 96, PixelFormats.Pbgra32); target.Render(page.Visual);
            Check(Pixel(target, 10, 10) == Colors.White, "Printer margin remains white");
            var expected = i == 0 ? Colors.Red : Colors.Green;
            Check(Pixel(target, 300, 400) == expected, "Paginator uses selected unsaved working images in correct order");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target)); using var output = File.Create(Path.Combine(folder, $"preview-{i + 1}.png")); png.Save(output);
        }
        var xpsPath = Path.Combine(folder, "selected-pages.xps");
        if (File.Exists(xpsPath)) File.Delete(xpsPath);
        using (var xps = new XpsDocument(xpsPath, FileAccess.ReadWrite)) XpsDocument.CreateXpsDocumentWriter(xps).Write(paginator);
        using (var xps = new XpsDocument(xpsPath, FileAccess.Read))
        {
            var document = xps.GetFixedDocumentSequence(); document.DocumentPaginator.ComputePageCount();
            Check(document.DocumentPaginator.PageCount == 2, "XPS pipeline serialized the selected pages");
        }
        File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: document acquisition-source tracking; print ranges; conservative unique/ambiguous MFP matching; IP identity/conflicts; aspect-preserving printer margins; unsaved source images; two-page XPS serialization.\nSoftware rendering/XPS checks only. No physical printer job was submitted.\n");
        Console.WriteLine("PASS printing range/matcher/paginator/rendering/XPS checks (software only; no physical printer job)");
    }

    private static void Image(string path, Color color)
    {
        var pixels = new byte[600 * 1200 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(600, 1200, 96, 96, PixelFormats.Bgra32, null, pixels, 600 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var pixel = new byte[4]; source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.001;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
