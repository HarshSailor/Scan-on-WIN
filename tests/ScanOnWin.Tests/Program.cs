using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Scan;
using NAPS2.Images;
using ScanOnWin.Core;
using UglyToad.PdfPig;

namespace ScanOnWin.Tests;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int _failures;
    private static string _root = "";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task Test(string name, Func<Task> run)
    {
        try { await run(); Console.WriteLine("PASS " + name); Results.Add(new { name, passed = true }); }
        catch (Exception e) { _failures++; Console.WriteLine("FAIL " + name + ": " + e); Results.Add(new { name, passed = false, error = e.GetType().Name, detail = e.Message }); }
    }
    private static ScanRequest Request => new(PaperSource.Feeder, 300, BitDepth.Color, PageSize.A4, false, false, IntPtr.Zero);
    public static async Task<int> Main(string[] args)
    {
        _root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/test-results");
        Directory.CreateDirectory(_root);
        var runFolder = Path.Combine(_root, DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(runFolder);
        var workerArg = Array.IndexOf(args, "--native-worker");
        using var runtime = new ScanningRuntime(Path.Combine(runFolder, "runtime"), workerArg >= 0 ? args[workerArg + 1] : null);
        var ocr = new OcrService(runtime.Context, Path.Combine(AppContext.BaseDirectory, "tessdata"));
        var exporter = new DocumentExporter(new Core.PdfExporter(runtime.Context, ocr), ocr);
        var document = new DocumentManager(Path.Combine(runFolder, "document"));
        var acquisition = Path.Combine(runFolder, "acquired");
        ExportOptions Options(string format, string name = "Scan") => new(Path.Combine(runFolder, "output"), name, format, 85, false, false, "rus+eng");

        await Test("MockScanner: single page", async () =>
        {
            var mock = new MockScanner();
            await foreach (var image in mock.Scan(mock.Device, Request, acquisition, default)) document.Add(image);
            Check(document.Pages.Count == 1 && File.Exists(document.Pages[0].CurrentPath), "No page");
        });
        await Test("MockScanner: multipage ADF + duplex options", async () =>
        {
            var mock = new MockScanner(2);
            await foreach (var image in mock.Scan(mock.Device, Request with { Source = PaperSource.Duplex }, acquisition, default)) document.Add(image);
            Check(document.Pages.Count == 3, "Page count");
        });
        await Test("MockScanner: cancellation retains completed page", async () =>
        {
            var mock = new MockScanner(10); using var cancel = new CancellationTokenSource(); var count = 0;
            await Throws<OperationCanceledException>(async () =>
            {
                await foreach (var image in mock.Scan(mock.Device, Request, acquisition, cancel.Token)) { count++; cancel.Cancel(); }
            });
            Check(count == 1, "Cancellation boundary");
        });
        await Test("Unavailable scanner and unsupported options", async () =>
        {
            var mock = new MockScanner(1, new IOException("Offline"));
            await Throws<IOException>(async () => { await foreach (var image in mock.Scan(mock.Device, Request, acquisition, default)) { } });
            await Throws<NotSupportedException>(async () => { await foreach (var image in mock.Scan(mock.Device, Request with { Dpi = 123 }, acquisition, default)) { } });
            Check(document.Pages.Count == 3, "Previous pages lost");
        });
        await Test("Save format: extension follows selection", () => ExportFormatChecks.VerifyTargetPaths(Path.Combine(runFolder, "format-targets")));
        await Test("Save format: actual PDF/PNG/JPG output", () => ExportFormatChecks.VerifyActualFormats(exporter, document.Pages, Path.Combine(runFolder, "format-change-output")));
        await Test("TWAIN x64 capability protocol round trip", () =>
        {
            var encoded = JsonSerializer.Serialize(Twain64Caps.From(MockScanner.Caps));
            var decoded = JsonSerializer.Deserialize<Twain64Caps>(encoded)!.ToCaps();
            ScanCapabilities.Validate(decoded, Request);
            Check(decoded.FlatbedCaps!.PageSizeCaps!.ScanArea!.WidthInMm == PageSize.A4.WidthInMm, "x64 scan area");
            Check(decoded.FeederCaps!.BitDepthCaps!.SupportsColor && decoded.DuplexCaps!.DpiCaps!.Values!.Contains(600), "x64 capabilities");
            return Task.CompletedTask;
        });
        await Test("Document recovery, rotate, crop, adjustments, undo and reorder", () =>
        {
            var page = document.Pages[0]; var original = page.CurrentPath;
            document.Replace(page, (b, p) => ScanProcessing.Rotate(b, p, 90));
            Check(page.PixelWidth == 2339 && page.PixelHeight == 1654, "Rotate size");
            document.Replace(page, (b, p) => ScanProcessing.Crop(b, p, new Rectangle(20, 30, 900, 1100)));
            Check(page.PixelWidth == 900 && page.PixelHeight == 1100, "Crop size");
            document.Replace(page, (b, p) => ScanProcessing.Adjust(b, p, 12, 20));
            document.Undo(page); document.Undo(page); document.Undo(page);
            Check(page.CurrentPath == original && page.PixelWidth == 1654, "Undo");
            document.Move(0, 2); Check(document.Pages[2].Id == page.Id, "Move");
            var restored = new DocumentManager(document.WorkingFolder); Check(restored.Restore() && restored.Pages.Count == 3, "Recovery");
            document.Move(2, 0); return Task.CompletedTask;
        });
        await Test("Failed edit preserves source and recovery manifest", async () =>
        {
            var page = document.Pages[0]; var path = page.CurrentPath;
            await Throws<ArgumentException>(() => { document.Replace(page, (b, p) => ScanProcessing.Crop(b, p, Rectangle.Empty)); return Task.CompletedTask; });
            Check(page.CurrentPath == path && File.Exists(path), "Source changed");
        });
        await Test("PNG/JPG numbered exports and image integrity", async () =>
        {
            foreach (var format in new[] { "PNG", "JPG" })
            {
                var paths = await exporter.Export(document.Pages, Options(format), null, default);
                Check(paths.Count == 3 && paths[0].EndsWith("Scan_001." + format.ToLowerInvariant()), "Sequence naming");
                using var image = new Bitmap(paths[0]); Check(image.Width == 1654 && image.Height == 2339, "Export size");
                if (format == "PNG") { using var source = new Bitmap(document.Pages[0].CurrentPath); Check(image.GetPixel(150, 200) == source.GetPixel(150, 200), "PNG pixels"); }
            }
        });
        await Test("Export collision and cancellation preserve document", async () =>
        {
            await Throws<IOException>(() => exporter.Export(document.Pages, Options("PNG"), null, default));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Throws<OperationCanceledException>(() => exporter.Export(document.Pages, Options("JPG", "Cancelled"), null, cancel.Token));
            Check(!Directory.EnumerateFiles(Options("JPG").Folder, "Cancelled*").Any(), "Cancelled output present");
            Check(document.Pages.All(p => File.Exists(p.CurrentPath)), "Document lost");
        });
        await Test("PDF single/multipage and lossless image size", async () =>
        {
            var single = await exporter.Export(document.Pages.Take(1).ToArray(), Options("PDF", "Single"), null, default);
            using var one = PdfDocument.Open(single[0]); Check(one.NumberOfPages == 1, "Single PDF");
            var multi = await exporter.Export(document.Pages, Options("PDF", "Multi"), null, default);
            using var pdf = PdfDocument.Open(multi[0]); Check(pdf.NumberOfPages == 3, "Multi PDF");
            var page = pdf.GetPage(1); Check(Math.Abs((double)page.Width - 595.44) < 2, "Physical page dimensions");
            Check(page.NumberOfImages == 1, "Original image absent");
        });
        await Test("Local OCR: English", async () => { var text = await ocr.Recognize(document.Pages[0].CurrentPath, "eng", default); Check(text.Contains("Hello", StringComparison.OrdinalIgnoreCase), "English OCR"); });
        await Test("Local OCR: Russian", async () => { var text = await ocr.Recognize(document.Pages[0].CurrentPath, "rus", default); Check(text.Contains("Привет", StringComparison.OrdinalIgnoreCase), "Russian OCR"); });
        await Test("Local OCR: bilingual + TXT multipage", async () =>
        {
            var paths = await exporter.Export(document.Pages, Options("TXT", "Recognized"), null, default);
            var text = File.ReadAllText(paths[0]); Check(text.Contains("Hello") && text.Contains("Привет"), "Bilingual TXT");
            Check(text.Count(c => c == '\f') == 2, "TXT page boundaries");
        });
        await Test("Searchable PDF: Cyrillic/Latin extraction, coordinates and original scans", async () =>
        {
            var paths = await exporter.Export(document.Pages, Options("PDF", "Searchable") with { Ocr = true }, null, default);
            using var pdf = PdfDocument.Open(paths[0]); Check(pdf.NumberOfPages == 3, "Searchable page count");
            foreach (var page in pdf.GetPages())
            {
                Check(page.Text.Contains("Hello") && page.Text.Contains("Привет"), "Searchable text extraction");
                Check(page.NumberOfImages == 1, "Original scan replaced");
                Check(page.Letters.All(l => l.GlyphRectangle.Left >= 0 && l.GlyphRectangle.Right <= page.Width + 1), "OCR coordinates");
            }
        });
        await Test("Compressed PDF and OCR failure", async () =>
        {
            var paths = await exporter.Export(document.Pages, Options("PDF", "Compressed") with { CompressPdf = true, Quality = 60 }, null, default);
            using var pdf = PdfDocument.Open(paths[0]); Check(pdf.NumberOfPages == 3, "Compressed export");
            var missing = new OcrService(runtime.Context, Path.Combine(runFolder, "missing-models"));
            await Throws<OcrException>(() => missing.Recognize(document.Pages[0].CurrentPath, "rus", default));
            runtime.Context.OcrEngine = NAPS2.Ocr.TesseractOcrEngine.Bundled(Path.Combine(AppContext.BaseDirectory, "tessdata"));
        });
        await Test("Settings corruption, validation and diagnostic privacy", async () =>
        {
            var settings = new SettingsService(runFolder); settings.Save(new AppSettings { Quality = 73 }); Check(settings.Load().Quality == 73, "Settings round trip");
            File.WriteAllText(Path.Combine(runFolder, "settings.json"), "{broken"); Check(settings.Load().Dpi == 300, "Settings recovery");
            var logs = new LoggingService(Path.Combine(runFolder, "logs")); logs.Error("test", new Exception("PRIVATE DOCUMENT SECRET"));
            Check(!File.ReadAllText(Path.Combine(runFolder, "logs", "diagnostics.log")).Contains("PRIVATE"), "Document leaked into log");
            await Throws<ArgumentException>(() => { DocumentExporter.TargetPaths(3, Options("PNG", "../escape")); return Task.CompletedTask; });
            await Throws<ArgumentException>(() => { DocumentExporter.TargetPaths(3, Options("PNG", "CON")); return Task.CompletedTask; });
            Check(FriendlyError.Describe(new OperationCanceledException()).Contains("отменена"), "Cancellation message");
            Check(FriendlyError.Describe(new UnauthorizedAccessException()).Contains("доступа"), "Access error");
        });
        if (args.Contains("--discover")) await Test("Real API discovery (no physical acquisition)", async () =>
        {
            var report = await new ScannerDiscovery(runtime.Backends, new LoggingService(Path.Combine(runFolder, "discovery-logs"))).Discover(null, default);
            AtomicFile.WriteJson(Path.Combine(runFolder, "discovery.json"), new { count = report.Devices.Count, backends = report.Devices.Select(d => d.Backend.Label), failed = report.FailedBackends });
            Console.WriteLine($"Actual discovery: {report.Devices.Count} device interfaces; unavailable backends: {string.Join(", ", report.FailedBackends)}");
        });
        AtomicFile.WriteJson(Path.Combine(_root, "results.json"), new { time = DateTimeOffset.Now, failures = _failures, results = Results, physicalHardwareScanningTested = false, outputs = runFolder });
        Console.WriteLine($"Results: {Results.Count - _failures}/{Results.Count} passed. Physical scanner acquisition: NOT TESTED.");
        return _failures == 0 ? 0 : 1;
    }
}
