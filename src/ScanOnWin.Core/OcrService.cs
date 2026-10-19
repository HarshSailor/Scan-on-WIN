using NAPS2.Ocr;
using NAPS2.Scan;
using System.Text;

namespace ScanOnWin.Core;

public sealed class OcrService
{
    private readonly ScanningContext _context;
    private readonly TesseractOcrEngine _engine;
    private readonly string _dataFolder;
    private int _failed;
    public OcrService(ScanningContext context, string dataFolder)
    {
        _context = context; _dataFolder = dataFolder;
        _engine = TesseractOcrEngine.Bundled(dataFolder);
        _engine.OcrError += (_, _) => Interlocked.Exchange(ref _failed, 1);
        _engine.OcrTimeout += (_, _) => Interlocked.Exchange(ref _failed, 1);
        context.OcrEngine = _engine;
    }
    public void Begin(string language)
    {
        if (language is not ("eng" or "rus" or "rus+eng")) throw new ArgumentException("OCR language");
        foreach (var code in language.Split('+'))
            if (!File.Exists(Path.Combine(_dataFolder, code + ".traineddata"))) throw new OcrException("Missing language data");
        Interlocked.Exchange(ref _failed, 0);
    }
    public void EnsureSuccessful() { if (Volatile.Read(ref _failed) != 0) throw new OcrException("OCR engine failure"); }
    public async Task<string> Recognize(string imagePath, string language, CancellationToken cancellationToken)
    {
        Begin(language);
        try
        {
            var result = await _engine.ProcessImage(_context, imagePath, new OcrParams(language, TimeoutInSeconds: 300), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSuccessful();
            if (result == null) throw new OcrException("No OCR result");
            return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
        }
        catch (Exception e) when (e is not (OperationCanceledException or OcrException)) { throw new OcrException("Recognition failed", e); }
    }
    public async Task ExportText(string temporaryPath, IReadOnlyList<DocumentPage> pages, string language,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(true));
        for (int i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0) await writer.WriteLineAsync("\n\f\n");
            await writer.WriteLineAsync(await Recognize(pages[i].CurrentPath, language, cancellationToken));
            progress?.Report((i + 1) * 100 / pages.Count);
        }
    }
}
