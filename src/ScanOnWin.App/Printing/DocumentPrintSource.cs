namespace ScanOnWin.App.Printing;

/// <summary>Tracks only the successful acquisition source of this in-memory document.</summary>
public sealed class DocumentPrintSource
{
    private bool _unknown;
    private string? _sourceKey;
    public string? ScannerName { get; private set; }
    public string? ScannerId { get; private set; }
    public DocumentPrintSource(bool hasRecoveredPages) => _unknown = hasRecoveredPages;
    public void PageAdded(string sourceKey, string scannerName, string? scannerId)
    {
        if (_unknown) return;
        if (_sourceKey != null && _sourceKey != sourceKey) { MarkUnknown(); return; }
        _sourceKey = sourceKey; ScannerName = scannerName; ScannerId = scannerId;
    }
    public void MarkUnknown() { _unknown = true; _sourceKey = null; ScannerName = null; ScannerId = null; }
    public void Reset() { _unknown = false; _sourceKey = null; ScannerName = null; ScannerId = null; }
}
