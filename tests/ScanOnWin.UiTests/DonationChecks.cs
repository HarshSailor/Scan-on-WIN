using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanOnWin.App;

internal static class DonationChecks
{
    public static void Run(string root)
    {
        const string expectedUrl = "https://messenger.sbrf.ru/sl/313SQtDcsRAqHIi4h";
        ProcessStartInfo? requested = null;
        var launches = 0;
        var browser = new BrowserLinkService(startInfo => { requested = startInfo; launches++; });
        Check(browser.TryOpenDonation(out var error) && error == null, "Donation link launch result");
        Check(BrowserLinkService.DonationUrl == expectedUrl && requested?.FileName == expectedUrl,
            "Donation link must use the exact requested HTTPS address");
        Check(requested!.UseShellExecute, "Donation link must use the Windows default URL handler");

        var unavailableBrowser = new BrowserLinkService(_ => throw new Win32Exception("No browser handler"));
        Check(!unavailableBrowser.TryOpenDonation(out error) &&
            error != null && error.Contains("Не удалось открыть браузер"), "Donation shell error handling");

        var window = new DonationWindow(browser);
        try
        {
            window.Show(); Pump();
            Check(window.Title == "Помощь разработчику", "Donation dialog title");
            Check(Descendants<TextBlock>(window).Any(text => text.Text == "Поддержать разработку Scan on WIN"),
                "Donation dialog explanatory text");
            var donate = window.FindName("DonateButton") as Button;
            Check(donate != null && donate.IsEnabled, "Donation button available");
            donate!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(launches == 2 && requested?.FileName == expectedUrl && requested.UseShellExecute,
                "Donation button must open the requested link through the shell");
            Check(window.IsVisible, "Donation dialog stays open after the browser launch");
            Capture(window, Path.Combine(root, "donation.png"));

            var close = Descendants<Button>(window).Single(button => Equals(button.Content, "Закрыть"));
            Check(close.IsCancel, "Donation dialog supports Escape");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!window.IsVisible, "Donation Close button closes the window");
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
            (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
