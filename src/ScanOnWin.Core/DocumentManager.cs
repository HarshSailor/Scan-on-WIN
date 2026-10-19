using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScanOnWin.Core;

public sealed class DocumentPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalPath { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    public string AcquisitionKey { get; set; } = "";
    public List<string> History { get; set; } = new();
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public double Dpi { get; set; } = 300;
}

public sealed class DocumentManager
{
    private sealed class Manifest
    {
        public List<DocumentPage> Pages { get; set; } = new();
        public bool Unsaved { get; set; }
    }
    private readonly List<DocumentPage> _pages = new();
    private readonly string _manifestPath;
    public string WorkingFolder { get; }
    public IReadOnlyList<DocumentPage> Pages => _pages;
    public bool Unsaved { get; private set; }
    public DocumentManager(string workingFolder)
    {
        WorkingFolder = Path.GetFullPath(workingFolder);
        Directory.CreateDirectory(WorkingFolder);
        _manifestPath = Path.Combine(WorkingFolder, "document.json");
    }
    public bool Restore()
    {
        if (!File.Exists(_manifestPath)) return false;
        var data = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_manifestPath));
        if (data == null) return false;
        // Only read application-owned local image paths; reject manifests pointing elsewhere.
        foreach (var page in data.Pages)
        {
            if (!Owned(page.OriginalPath) || !Owned(page.CurrentPath) || !File.Exists(page.CurrentPath))
                throw new IOException("Invalid recovery manifest");
            page.History = page.History.Where(p => Owned(p) && File.Exists(p)).ToList();
        }
        _pages.Clear();
        _pages.AddRange(data.Pages);
        Unsaved = data.Unsaved;
        return _pages.Count > 0;
    }
    private bool Owned(string path) => Path.GetFullPath(path).StartsWith(WorkingFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public DocumentPage Add(string acquiredPath, double fallbackDpi = 300)
    {
        var acquisitionKey = Path.GetFileName(acquiredPath);
        var existing = _pages.FirstOrDefault(p => p.AcquisitionKey == acquisitionKey);
        if (existing != null) return existing;
        var dest = NewImagePath();
        using (var source = new Bitmap(acquiredPath))
        {
            if (source.HorizontalResolution < 30) source.SetResolution((float)fallbackDpi, (float)fallbackDpi);
            source.Save(dest, ImageFormat.Png);
        }
        using var bitmap = new Bitmap(dest);
        var page = new DocumentPage { OriginalPath = dest, CurrentPath = dest, AcquisitionKey = acquisitionKey, PixelWidth = bitmap.Width, PixelHeight = bitmap.Height,
            Dpi = bitmap.HorizontalResolution >= 30 ? bitmap.HorizontalResolution : fallbackDpi };
        _pages.Add(page);
        try { Changed(); } catch { _pages.Remove(page); throw; }
        return page;
    }
    public void Replace(DocumentPage page, Action<Bitmap, string> edit)
    {
        if (!_pages.Contains(page)) throw new ArgumentException("Unknown page");
        var dest = NewImagePath();
        using (var bitmap = new Bitmap(page.CurrentPath)) edit(bitmap, dest);
        using var result = new Bitmap(dest);
        var previous = page.CurrentPath;
        var oldWidth = page.PixelWidth; var oldHeight = page.PixelHeight;
        page.History.Add(previous);
        page.CurrentPath = dest;
        page.PixelWidth = result.Width; page.PixelHeight = result.Height;
        try { Changed(); }
        catch { page.CurrentPath = previous; page.PixelWidth = oldWidth; page.PixelHeight = oldHeight; page.History.RemoveAt(page.History.Count - 1); throw; }
    }
    public void Undo(DocumentPage page)
    {
        if (page.History.Count == 0) return;
        var last = page.History[^1];
        var previous = page.CurrentPath;
        page.History.RemoveAt(page.History.Count - 1); page.CurrentPath = last;
        using var bitmap = new Bitmap(last);
        page.PixelWidth = bitmap.Width; page.PixelHeight = bitmap.Height;
        try { Changed(); }
        catch { page.CurrentPath = previous; page.History.Add(last); throw; }
    }
    public void Delete(DocumentPage page)
    {
        var index = _pages.IndexOf(page);
        if (index < 0) return;
        _pages.RemoveAt(index);
        try { Changed(); } catch { _pages.Insert(index, page); throw; }
    }
    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _pages.Count || to >= _pages.Count) return;
        var page = _pages[from]; _pages.RemoveAt(from); _pages.Insert(to, page);
        try { Changed(); } catch { _pages.RemoveAt(to); _pages.Insert(from, page); throw; }
    }
    public void Clear()
    {
        var previous = _pages.ToList(); _pages.Clear();
        try { Changed(); } catch { _pages.AddRange(previous); throw; }
        // Only remove images after the empty manifest is durable.
        foreach (var file in Directory.EnumerateFiles(WorkingFolder, "*.png"))
            try { File.Delete(file); } catch (IOException) { }
    }
    public void MarkSaved() { Unsaved = false; Persist(); }
    public string NewImagePath() => Path.Combine(WorkingFolder, Guid.NewGuid().ToString("N") + ".png");
    private void Changed() { Unsaved = true; Persist(); }
    private void Persist() => AtomicFile.WriteJson(_manifestPath, new Manifest { Pages = _pages, Unsaved = Unsaved });
}
