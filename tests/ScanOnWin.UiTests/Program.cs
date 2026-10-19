using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanOnWin.App;
using ScanOnWin.Core;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-tests");
        Directory.CreateDirectory(root);
        var app = new App(suppressShellStartup: true); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.DispatcherUnhandledException += (_, e) => { Console.Error.WriteLine(e.Exception); Environment.ExitCode = 1; };
        try
        {
            PrintingChecks.Run(root);
            DonationChecks.Run(root);
            if (args.Contains("--open-donation"))
            {
                Check(new BrowserLinkService().TryOpenDonation(out var linkError), linkError ?? "Windows browser launch");
                File.WriteAllText(Path.Combine(root, "browser-launch.txt"), "Windows default HTTPS handler launched for " + BrowserLinkService.DonationUrl + "\n");
            }
            var fixture = Path.Combine(root, "ui-fixture.png");
            using (var bitmap = new Bitmap(1240, 1754))
            {
                bitmap.SetResolution(150, 150);
                using var g = Graphics.FromImage(bitmap); g.Clear(System.Drawing.Color.White);
                using var title = new Font("Arial", 48, System.Drawing.FontStyle.Bold);
                using var text = new Font("Arial", 23);
                g.DrawString("Scan on WIN", title, System.Drawing.Brushes.Black, 110, 140);
                g.DrawString("Пример отсканированной страницы", text, System.Drawing.Brushes.Black, 110, 280);
                g.DrawString("Тест интерфейса / UI test", text, System.Drawing.Brushes.Black, 110, 340);
                g.DrawLine(System.Drawing.Pens.Teal, 110, 410, 1110, 410);
                for (int i = 0; i < 12; i++) g.DrawString($"Строка документа {i + 1}. Hello world.", text, System.Drawing.Brushes.DimGray, 110, 480 + i * 65);
                bitmap.Save(fixture, ImageFormat.Png);
            }
            var data = Path.Combine(root, "data-" + Guid.NewGuid().ToString("N"));
            var document = new DocumentManager(Path.Combine(data, "document")); document.Add(fixture);
            var secondFixture = Path.Combine(root, "ui-fixture-2.png"); File.Copy(fixture, secondFixture, true); document.Add(secondFixture);
            var vm = new MainViewModel(data) { OpenFolder = false, OutputFolder = Path.Combine(data, "export"), FileName = "UI-document.pdf" };
            Check(vm.Format == "PDF", "Default save format must be PDF");
            var window = new MainWindow(vm, initializeOnLoad: false); window.Show(); Pump();
            Check(vm.Pages.Count == 2 && vm.PreviewImage != null, "Recovered pages not visible");
            Check(!vm.ScanCommand.CanExecute(null) && vm.SaveCommand.CanExecute(null) && vm.PrintCommand.CanExecute(null), "No-scanner command state");
            Capture(window, Path.Combine(root, "light.png"));
            vm.DarkTheme = true; Pump(); Capture(window, Path.Combine(root, "dark.png"));
            window.Width = 1020; window.Height = 680; Pump(); Capture(window, Path.Combine(root, "small-window.png"));
            vm.RotateRightCommand.Execute(null); Wait(() => !vm.IsBusy);
            Check(vm.SelectedPage!.Page.PixelWidth == 1754, "UI rotation command");
            vm.UndoCommand.Execute(null); Wait(() => !vm.IsBusy); Check(vm.SelectedPage!.Page.PixelWidth == 1240, "UI undo command");
            var formats = (ComboBox)window.FindName("SaveFormatComboBox");
            foreach (var format in new[] { "PNG", "JPG", "PDF" })
            {
                SelectFormat(formats, format); Check(vm.Format == format, "Format dropdown binding: " + format);
                var freshSettings = new MainViewModel(data); Check(freshSettings.Format == format, "Last chosen format persisted: " + format); freshSettings.Dispose();
                vm.SaveCommand.Execute(null); Wait(() => !vm.IsBusy);
                Check(Directory.GetFiles(vm.OutputFolder, "*." + format.ToLowerInvariant()).Length == (format == "PDF" ? 1 : 2), "UI save routes selected format: " + format + "; " + vm.Status);
            }
            Check(!Directory.GetFiles(vm.OutputFolder).Any(p => Path.GetFileName(p).Contains(".pdf.")), "Typed extension must be replaced");
            if (args.Contains("--installed-printers")) InstalledPrinterChecks.Run(window, document.Pages, root);
            window.Close();
            var empty = new MainViewModel(Path.Combine(root, "empty-" + Guid.NewGuid().ToString("N")));
            var emptyWindow = new MainWindow(empty, false); emptyWindow.Show(); Pump();
            Check(!empty.ScanCommand.CanExecute(null) && !empty.SaveCommand.CanExecute(null) && !empty.PrintCommand.CanExecute(null), "Empty command state");
            Capture(emptyWindow, Path.Combine(root, "empty.png")); emptyWindow.Close();
            File.WriteAllText(Path.Combine(root, "result.txt"), "PASS: WPF startup, recovery preview, light/dark/small layouts, rotate, undo, actual dropdown selection and PDF/PNG/JPG save, persisted format, donation dialog/link routing, software XPS printing, empty/no-scanner controls.\nPhysical printing: not tested. Browser launch: injected in automated tests.\n");
            Console.WriteLine("PASS WPF startup, rendering, commands and no-scanner state");
            app.Shutdown(0); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); return 1; }
    }
    private static void SelectFormat(ComboBox combo, string value)
    {
        combo.BringIntoView(); Pump(); combo.ApplyTemplate();
        var toggle = (ToggleButton)combo.Template.FindName("DropToggle", combo);
        Check(combo.IsEnabled && toggle.IsEnabled, $"Format control disabled: combo={combo.IsEnabled}, toggle={toggle.IsEnabled}, vm={((MainViewModel)combo.DataContext).CanChooseSaveFormat}, busy={((MainViewModel)combo.DataContext).IsBusy}");
        ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle(); Pump();
        Check(combo.IsDropDownOpen, "Format dropdown did not open");
        var peer = new ComboBoxAutomationPeer(combo).GetChildren().Single(p => p.GetName() == value);
        ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select(); Pump();
        combo.IsDropDownOpen = false; Pump();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Wait(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!done()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("UI operation"); Pump(); Thread.Sleep(10); }
        Pump();
    }
    private static void Capture(Window window, string path)
    {
        Pump(); window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var target = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

