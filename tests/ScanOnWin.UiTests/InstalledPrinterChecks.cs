using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanOnWin.App.Printing;
using ScanOnWin.Core;

/// <summary>Opt-in --installed-printers checks. Reads installed queues and opens preview; never submits a print job.</summary>
internal static class InstalledPrinterChecks
{
    public static void Run(Window owner, IReadOnlyList<DocumentPage> pages, string root)
    {
        Check(pages.Count > 0, "Installed printer preview requires a working image");
        var folder = Path.Combine(root, "installed-printers"); Directory.CreateDirectory(folder);
        using var server = new LocalPrintServer();
        using var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });
        var installed = queues.Select(q => new PrinterDescriptor(q.Name, q.QueueDriver?.Name ?? "", q.QueuePort?.Name ?? "", q.Location ?? "", q.Comment ?? "")).ToArray();
        var match = PrinterMatcher.Match("Samsung SCX-4200 Series", null, installed);
        Check(match.Printer != null && !match.IsAmbiguous, "Installed Samsung SCX-4200 printer must have one unambiguous match");
        File.WriteAllLines(Path.Combine(folder, "queues.txt"), installed.Select(q => $"{q.Name} | {q.DriverName} | {q.PortName}"));
        var selectedQueue = queues.First(q => q.Name == match.Printer!.Name);
        var capabilities = selectedQueue.GetPrintCapabilities(selectedQueue.UserPrintTicket ?? selectedQueue.DefaultPrintTicket);
        // Repeat a working source only inside this preview fixture, without mutating the application document.
        IReadOnlyList<DocumentPage> previewPages = pages.Count >= 3 ? pages : new[] { pages[0], pages[0], pages[0] };
        Exception? failure = null; bool inspected = false; int stage = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var preview = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Печать — Scan on WIN");
            if (preview == null) return;
            try
            {
                var printer = Named<ComboBox>(preview, "Принтер печати");
                var selectedName = printer.SelectedItem?.GetType().GetProperty("Name")?.GetValue(printer.SelectedItem)?.ToString();
                Check(selectedName == match.Printer!.Name, "Preview must propose the installed scanning MFP printing queue");
                var scope = Named<ComboBox>(preview, "Страницы для печати");
                var range = Named<TextBox>(preview, "Номера страниц для печати");
                var documentView = Descendants<DocumentPageView>(preview).Single();
                var paper = Named<ComboBox>(preview, "Бумага для печати");
                var orientation = Named<ComboBox>(preview, "Ориентация печати");
                var color = Named<ComboBox>(preview, "Цвет печати");
                var copies = Named<TextBox>(preview, "Копии печати");
                Check(paper.Items.Count == capabilities.PageMediaSizeCapability.Count(s => s.Width > 0 && s.Height > 0), "Paper choices reflect real printer capabilities");
                Check(orientation.Items.Count == capabilities.PageOrientationCapability.Count, "Orientation choices reflect real printer capabilities");
                Check(color.Items.Count == capabilities.OutputColorCapability.Count, "Color choices reflect real printer capabilities");
                Check(int.TryParse(copies.Text, out var count) && count >= 1, "Copies have a valid printer default");
                if (stage == 0)
                {
                    Check(documentView.DocumentPaginator.PageCount == previewPages.Count, "All pages preview count");
                    Capture(preview, Path.Combine(folder, "preview-all.png")); scope.SelectedIndex = 1; stage++; return;
                }
                if (stage == 1)
                {
                    Check(documentView.DocumentPaginator.PageCount == 1, "Current page scope produces one page");
                    Capture(preview, Path.Combine(folder, "preview-current.png")); range.Text = "1,3"; scope.SelectedIndex = 2; stage++; return;
                }
                Check(documentView.DocumentPaginator.PageCount == 2, "Non-contiguous custom scope produces two pages");
                Capture(preview, Path.Combine(folder, "preview-selected.png")); inspected = true; timer.Stop(); preview.Close();
            }
            catch (Exception error) { failure = error; timer.Stop(); preview.Close(); }
        };
        timer.Start();
        try { PrintService.Show(owner, previewPages, 1, "Samsung SCX-4200 Series"); }
        finally { timer.Stop(); }
        if (failure != null) throw new InvalidOperationException("Installed printer preview check failed", failure);
        Check(inspected, "Installed printer modal was inspected and closed");
        File.WriteAllText(Path.Combine(folder, "result.txt"), $"PASS: real installed queue enumeration; unique Samsung MFP match; printer capability options; all/current/non-contiguous page preview.\nProposed queue: {match.Printer!.Name}.\nNo print button was pressed. No print job was submitted. Physical printing was not tested.\n");
        Console.WriteLine("PASS installed Samsung printer metadata/options/preview (no job submitted)");
    }

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Descendants<T>(root).Single(item => AutomationProperties.GetName(item) == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var target = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        target.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
