using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ScanOnWin.Core;
using UglyToad.PdfPig;

namespace ScanOnWin.Tests;

internal static class ExportFormatChecks
{
    public static Task VerifyTargetPaths(string folder)
    {
        foreach (var format in new[] { "PDF", "PNG", "JPG", "TXT" })
        foreach (var suffix in new[] { ".pdf", ".PNG", ".jpg", ".JPEG", ".txt", ".pdf.png" })
        {
            var options = new ExportOptions(folder, "Report.2026" + suffix, format, 85, false, false, "eng");
            var single = DocumentExporter.TargetPaths(1, options);
            Check(Path.GetFileName(single.Single()) == "Report.2026." + format.ToLowerInvariant(), "Existing suffix was not replaced");
            var multiple = DocumentExporter.TargetPaths(2, options);
            if (format is "PDF" or "TXT")
                Check(multiple.Count == 1 && multiple[0] == single[0], "Combined document target count");
            else
                Check(multiple.Select(Path.GetFileName).SequenceEqual(new[] { "Report.2026_001." + format.ToLowerInvariant(), "Report.2026_002." + format.ToLowerInvariant() }), "Per-page image names");
        }
        foreach (var invalid in new[] { ".pdf", "CON.pdf", "../escape.png" })
        {
            try
            {
                DocumentExporter.TargetPaths(1, new ExportOptions(folder, invalid, "PNG", 85, false, false, "eng"));
                throw new InvalidOperationException("Invalid name accepted: " + invalid);
            }
            catch (ArgumentException) { }
        }
        return Task.CompletedTask;
    }

    public static async Task VerifyActualFormats(DocumentExporter exporter, IReadOnlyList<DocumentPage> pages, string folder)
    {
        var selected = pages.Take(2).ToArray();
        Check(selected.Length == 2, "Two source pages are required");
        foreach (var format in new[] { "PDF", "PNG", "JPG" })
        {
            var options = new ExportOptions(Path.Combine(folder, format), "Changed.JPEG", format, 85, false, false, "eng");
            var paths = await exporter.Export(selected, options, null, default);
            Check(paths.All(p => Path.GetExtension(p) == "." + format.ToLowerInvariant()), "Selected extension not applied");
            if (format == "PDF")
            {
                Check(paths.Count == 1, "PDF must produce one file");
                using var pdf = PdfDocument.Open(paths[0]);
                Check(pdf.NumberOfPages == selected.Length, "PDF pages were not combined");
            }
            else
            {
                Check(paths.Count == selected.Length, "Image page count");
                for (var i = 0; i < paths.Count; i++)
                {
                    using var image = new Bitmap(paths[i]);
                    Check(image.RawFormat.Guid == (format == "PNG" ? ImageFormat.Png.Guid : ImageFormat.Jpeg.Guid), "Actual image encoding does not match selection");
                    Check(image.Width == selected[i].PixelWidth && image.Height == selected[i].PixelHeight, "Exported image dimensions changed");
                }
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
