using System.Diagnostics;

namespace ScanOnWin.App;

/// <summary>Opens the developer's public donation link with the Windows URL handler.</summary>
public sealed class BrowserLinkService
{
    public const string DonationUrl = "https://messenger.sbrf.ru/sl/313SQtDcsRAqHIi4h";
    private readonly Action<ProcessStartInfo> _launch;

    public BrowserLinkService(Action<ProcessStartInfo>? launch = null)
    {
        _launch = launch ?? (startInfo => { Process.Start(startInfo); });
    }

    public bool TryOpenDonation(out string? error)
    {
        try
        {
            _launch(new ProcessStartInfo(DonationUrl) { UseShellExecute = true });
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // The operating system may have no HTTPS handler, or reject launching it.
            // A failed shell launch must never escape the modal's click handler.
            error = "Не удалось открыть браузер по умолчанию.\n" + exception.Message;
            return false;
        }
    }
}
