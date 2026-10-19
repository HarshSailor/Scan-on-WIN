using System.Drawing;
using System.Drawing.Imaging;
using NAPS2.Images;
using NAPS2.Scan;
using NAPS2.ImportExport;
using NAPS2.Ocr;
using NAPS2.Util;

namespace ScanOnWin.Core;

public sealed record ExportOptions(string Folder, string BaseName, string Format, int Quality, bool CompressPdf, bool Ocr, string Language);

public static class ImageExporter
{
    public static void Write(string input, string output, string format, int quality)
    {
        if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        if (format == "PNG") { File.Copy(input, output, false); return; }
        if (format != "JPG") throw new ArgumentException("Image format");
        using var image = new Bitmap(input);
        var encoder = ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        image.Save(output, encoder, parameters);
    }
}

public sealed class PdfExporter(ScanningContext context, OcrService ocr)
{
    public async Task Write(string temporaryPath, IReadOnlyList<DocumentPage> pages, ExportOptions options,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (options.Ocr) ocr.Begin(options.Language);
        var images = new List<ProcessedImage>();
        var scratch = new List<string>();
        try
        {
            var importer = new ImageImporter(context);
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = page.CurrentPath;
                if (options.CompressPdf)
                {
                    input = Path.Combine(context.TempFolderPath, Guid.NewGuid().ToString("N") + ".jpg");
                    scratch.Add(input); ImageExporter.Write(page.CurrentPath, input, "JPG", options.Quality);
                }
                await foreach (var image in importer.Import(input, progress: new ProgressHandler(null, cancellationToken)))
                    images.Add(image);
            }
            var sdkExporter = new NAPS2.Pdf.PdfExporter(context);
            var completed = await sdkExporter.Export(temporaryPath, images,
                new NAPS2.Pdf.PdfExportParams { Metadata = new NAPS2.Pdf.PdfMetadata { Creator = "Scan on WIN", Title = options.BaseName } },
                options.Ocr ? new OcrParams(options.Language, TimeoutInSeconds: 300) : OcrParams.Empty,
                new ProgressHandler((current, total) => progress?.Report(total > 0 ? current * 100 / total : 0), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (!completed) throw new IOException("PDF export incomplete");
            if (options.Ocr) ocr.EnsureSuccessful();
        }
        finally
        {
            foreach (var image in images) image.Dispose();
            foreach (var file in scratch) if (File.Exists(file)) File.Delete(file);
        }
    }
}

public sealed class DocumentExporter(PdfExporter pdf, OcrService ocr)
{
    public static IReadOnlyList<string> TargetPaths(int count, ExportOptions options)
    {
        if (count <= 0) throw new ArgumentException("Empty document");
        var name = options.BaseName.Trim();
        // Users may type an extension or switch formats after naming the document.
        // Replace known export suffixes instead of appending a second extension.
        while (Path.GetExtension(name).ToLowerInvariant() is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt")
            name = name[..^Path.GetExtension(name).Length].TrimEnd();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name is "." or "..")
            throw new ArgumentException("Invalid filename");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
            throw new ArgumentException("Reserved filename");
        if (options.Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(options.Quality));
        if (options.Format is not ("PDF" or "PNG" or "JPG" or "TXT")) throw new ArgumentException("Format");
        var extension = options.Format.ToLowerInvariant();
        return options.Format is "PDF" or "TXT" || count == 1
            ? [Path.Combine(options.Folder, name + "." + extension)]
            : Enumerable.Range(1, count).Select(i => Path.Combine(options.Folder, $"{name}_{i:000}.{extension}")).ToArray();
    }
    public async Task<IReadOnlyList<string>> Export(IReadOnlyList<DocumentPage> pages, ExportOptions options,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var targets = TargetPaths(pages.Count, options);
        Directory.CreateDirectory(options.Folder);
        // No overwrite: the caller can choose a new name. Stage every file before committing any output.
        if (targets.Any(File.Exists)) throw new IOException("Target exists");
        var staging = Path.Combine(options.Folder, ".scan-on-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var staged = targets.Select(p => Path.Combine(staging, Path.GetFileName(p))).ToArray();
        var committed = new List<string>();
        try
        {
            if (options.Format == "PDF") await pdf.Write(staged[0], pages, options, progress, cancellationToken);
            else if (options.Format == "TXT") await ocr.ExportText(staged[0], pages, options.Language, progress, cancellationToken);
            else
                for (int i = 0; i < pages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ImageExporter.Write(pages[i].CurrentPath, staged[i], options.Format, options.Quality);
                    progress?.Report((i + 1) * 100 / pages.Count);
                }
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < targets.Count; i++) { File.Move(staged[i], targets[i], false); committed.Add(targets[i]); }
            return targets;
        }
        catch
        {
            foreach (var path in committed) try { File.Delete(path); } catch (IOException) { }
            throw;
        }
        finally
        {
            // This directory was created above, has a unique name, and only contains this export's scratch files.
            foreach (var path in staged) try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
            try { Directory.Delete(staging); } catch (IOException) { }
        }
    }
}
