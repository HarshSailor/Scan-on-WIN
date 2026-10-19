using System.Windows.Media;
using ScanOnWin.Core;

namespace ScanOnWin.App;

public partial class App : Application
{
    private readonly bool _suppressShellStartup;
    public App() { }
    public App(bool suppressShellStartup) => _suppressShellStartup = suppressShellStartup;
    private Mutex? _mutex;
    public string DataRoot { get; private set; } = "";
    protected override void OnStartup(StartupEventArgs e)
    {
        if (_suppressShellStartup) return;
        base.OnStartup(e);
        if (e.Args.Contains("--twain64-worker"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var parentArg = Array.IndexOf(e.Args, "--parent-pid");
            if (parentArg >= 0 && parentArg + 1 < e.Args.Length && int.TryParse(e.Args[parentArg + 1], out var parentId))
                _ = Task.Run(async () =>
                {
                    try { using var parent = System.Diagnostics.Process.GetProcessById(parentId); await parent.WaitForExitAsync(); }
                    catch (ArgumentException) { }
                    Environment.Exit(0);
                });
            _ = Task.Run(async () =>
            {
                var result = await Twain64Protocol.Run();
                await Dispatcher.InvokeAsync(() => Shutdown(result));
            });
            return;
        }
        DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScanOnWIN");
        var rootArg = Array.IndexOf(e.Args, "--data-root");
        if (rootArg >= 0 && rootArg + 1 < e.Args.Length) DataRoot = Path.GetFullPath(e.Args[rootArg + 1]);
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataRoot.ToLowerInvariant())))[..24];
        _mutex = new Mutex(true, "Local\\ScanOnWIN-" + identity, out var created);
        if (!created) { MessageBox.Show("Scan on WIN уже запущена.", "Scan on WIN"); Shutdown(); return; }
        var log = new LoggingService(Path.Combine(DataRoot, "logs"));
        DispatcherUnhandledException += (_, args) => { log.Error("ui", args.Exception); MessageBox.Show(FriendlyError.Describe(args.Exception), "Scan on WIN", MessageBoxButton.OK, MessageBoxImage.Information); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { log.Error("task", args.Exception); args.SetObserved(); };
        try { new MainWindow(new MainViewModel(DataRoot)).Show(); }
        catch (Exception error) { log.Error("startup", error); MessageBox.Show("Не удалось запустить программу. Проверьте целостность установки и доступ к папке данных.\n" + FriendlyError.Describe(error), "Scan on WIN", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    public static void ApplyTheme(bool dark)
    {
        var colors = dark ? new[] { "#161E27", "#202B36", "#111923", "#E6EDF4", "#A7B7C4", "#3B4A58", "#21A896", "#193D3C" }
            : new[] { "#F3F5F7", "#FFFFFF", "#E7ECF0", "#172A34", "#60717C", "#DCE3E8", "#0F766E", "#E1F4F0" };
        string[] keys = ["AppBackground", "PanelBackground", "PreviewBackground", "TextBrush", "MutedBrush", "BorderBrush", "AccentBrush", "AccentSoftBrush"];
        for (int i = 0; i < keys.Length; i++) Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Current.Resources[SystemColors.WindowBrushKey] = Current.Resources["PanelBackground"];
        Current.Resources[SystemColors.WindowTextBrushKey] = Current.Resources["TextBrush"];
    }
    protected override void OnExit(ExitEventArgs e) { _mutex?.Dispose(); base.OnExit(e); }
}
