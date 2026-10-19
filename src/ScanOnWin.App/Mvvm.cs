using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ScanOnWin.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}
public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed record Choice<T>(T Value, string Label);

public sealed class PageViewModel : Observable
{
    public ScanOnWin.Core.DocumentPage Page { get; }
    public System.Windows.Media.Imaging.BitmapImage Thumbnail { get; }
    private int _number;
    public int Number { get => _number; set { Set(ref _number, value); Changed(nameof(Label)); } }
    public string Label => $"Страница {Number}";
    public string Details => $"{Page.PixelWidth} × {Page.PixelHeight}";
    public PageViewModel(ScanOnWin.Core.DocumentPage page, int number)
    {
        Page = page; _number = number;
        Thumbnail = LoadBitmap(page.CurrentPath, 160);
    }
    public static System.Windows.Media.Imaging.BitmapImage LoadBitmap(string path, int width = 0)
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        using var stream = File.OpenRead(path);
        bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        if (width > 0) bitmap.DecodePixelWidth = width;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}
