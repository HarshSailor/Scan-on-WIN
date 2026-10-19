using System.Printing;
using ScanPage = ScanOnWin.Core.DocumentPage;

namespace ScanOnWin.App.Printing;

public static class PrintService
{
    public static string Show(Window owner, IReadOnlyList<ScanPage> pages, int selectedPageIndex, string? scannerName, string? scannerId = null)
    {
        if (pages.Count == 0) return "Сначала отсканируйте документ для печати.";
        var queues = new List<PrintQueue>();
        try
        {
            using var server = new LocalPrintServer();
            using var installed = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]);
            queues.AddRange(installed);
            if (queues.Count == 0)
            {
                MessageBox.Show(owner, "В Windows не найдено доступных принтеров. Установите или подключите принтер и повторите попытку.", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
                return "Нет установленных принтеров.";
            }
            var entries = queues.Select(queue => new PrinterEntry(queue, new PrinterDescriptor(
                Read(() => queue.Name), Read(() => queue.QueueDriver?.Name), Read(() => queue.QueuePort?.Name),
                Read(() => queue.Location), Read(() => queue.Comment)))).Where(entry => !string.IsNullOrWhiteSpace(entry.Info.Name)).OrderBy(entry => entry.Info.Name).ToArray();
            if (entries.Length == 0) throw new InvalidOperationException("Не удалось получить сведения о принтерах Windows. Проверьте службу «Диспетчер печати» и драйверы принтеров.");
            var match = PrinterMatcher.Match(scannerName, scannerId, entries.Select(entry => entry.Info).ToArray());
            var preview = new PrintPreviewWindow(pages, selectedPageIndex, entries, match) { Owner = owner };
            preview.ShowDialog();
            return preview.ResultStatus;
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, "Не удалось открыть печать. Проверьте подключение принтера и службу «Диспетчер печати».\n\n" + error.Message,
                "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Warning);
            return "Не удалось открыть печать: " + error.Message;
        }
        finally
        {
            foreach (var queue in queues) queue.Dispose();
        }
    }

    private static string Read(Func<string?> getter) { try { return getter() ?? ""; } catch (PrintSystemException) { return ""; } }
}

internal sealed record PrinterEntry(PrintQueue Queue, PrinterDescriptor Info)
{
    public string Name => Info.Name;
}
