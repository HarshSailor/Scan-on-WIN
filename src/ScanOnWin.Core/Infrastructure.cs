using System.Text.Json;
using System.Text;

namespace ScanOnWin.Core;

public static class AtomicFile
{
    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class AppSettings
{
    public string? DeviceKey { get; set; }
    public int Dpi { get; set; } = 300;
    public string PaperSource { get; set; } = "Flatbed";
    public string Color { get; set; } = "Color";
    public string PageSize { get; set; } = "A4";
    public double CustomWidth { get; set; } = 210;
    public double CustomHeight { get; set; } = 297;
    public string Format { get; set; } = "PDF";
    public int Quality { get; set; } = 90;
    public bool CompressPdf { get; set; }
    public bool Ocr { get; set; }
    public string OcrLanguage { get; set; } = "rus+eng";
    public string OutputFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public bool OpenFolder { get; set; } = true;
    public bool DarkTheme { get; set; }
    public bool AutoDeskew { get; set; }
    public bool NativeUi { get; set; }
    public List<string> NetworkAddresses { get; set; } = new();
}

public sealed class SettingsService(string root)
{
    public AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(root, "settings.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(AppSettings settings) => AtomicFile.WriteJson(Path.Combine(root, "settings.json"), settings);
}

public sealed class LoggingService(string root)
{
    private readonly object _gate = new();
    // Deliberately omit exception messages, file paths, scanner identifiers and OCR content.
    public void Error(string operation, Exception error)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".previous", true);
                var frames = new System.Diagnostics.StackTrace(error).GetFrames() ?? [];
                var errorType = error is ScannerBackendException backend ? backend.ErrorType : error.GetType().FullName;
                var errorCode = error is ScannerBackendException backendCode ? backendCode.Code : error.HResult;
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {operation} {errorType} 0x{errorCode:X8}\n" +
                    string.Join("\n", frames.Take(8).Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name)) + "\n", Encoding.UTF8);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Diagnostics must never lose a document. */ }
    }
}

public static class FriendlyError
{
    public static string Describe(Exception error, string? operation = null)
    {
        if (error is AggregateException a) return Describe(a.GetBaseException());
        if (error is OperationCanceledException) return "Операция отменена. Полученные страницы сохранены в текущем документе.";
        if (error is UnauthorizedAccessException) return operation is "scan" or "capabilities"
            ? "Windows или драйвер запретили доступ к сканеру. Проверьте подключение, доступность WIA и драйвер производителя."
            : "Нет доступа к папке. Выберите другую папку сохранения.";
        if (error is IOException io && (io.HResult & 0xFFFF) is 112 or 39) return "На диске недостаточно свободного места. Освободите место и повторите сохранение.";
        var name = error is ScannerBackendException backendError ? backendError.ErrorType : error.GetType().Name;
        if (name.Contains("PaperJam")) return "Замятие бумаги. Устраните замятие и добавьте оставшиеся страницы повторным сканированием.";
        if (name.Contains("NoPaper") || name.Contains("FeederEmpty")) return "В автоподатчике нет бумаги. Загрузите листы и повторите сканирование.";
        if (name.Contains("Busy")) return "Сканер занят. Закройте другие программы сканирования и повторите попытку.";
        if (name.Contains("Offline") || name.Contains("DeviceNotFound") || name.Contains("Connection")) return "Сканер недоступен. Проверьте питание, подключение и драйвер, затем обновите список устройств.";
        if (name.Contains("Driver") || name.Contains("Wia") || name.Contains("Twain")) return "Драйвер сканера сообщил об ошибке. Проверьте подключение, бумагу и драйвер производителя. Попробуйте другой интерфейс или окно драйвера.";
        if (error is TimeoutException) return "Устройство не ответило вовремя. Проверьте подключение и повторите операцию.";
        if (error is NotSupportedException or ArgumentException) return "Параметры не поддерживаются. Выберите допустимый источник, размер листа и разрешение или используйте окно драйвера.";
        if (error is OcrException) return "Не удалось распознать текст. Страницы сохранены. Проверьте наличие моделей OCR или сохраните документ с выключенным OCR.";
        if (error is IOException) return "Не удалось прочитать или записать файл. Проверьте доступ к папке и свободное место. Страницы остаются в документе.";
        return "Не удалось выполнить операцию. Страницы остаются в документе. Повторите попытку; сведения об ошибке записаны в журнал диагностики.";
    }
}

public sealed class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}
