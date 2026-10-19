namespace ScanOnWin.App;

public partial class DonationWindow : Window
{
    private readonly BrowserLinkService _browser;

    public DonationWindow(BrowserLinkService? browser = null)
    {
        _browser = browser ?? new BrowserLinkService();
        InitializeComponent();
    }

    private void DonateClick(object sender, RoutedEventArgs e)
    {
        if (!_browser.TryOpenDonation(out var error))
            MessageBox.Show(this, error, "Помощь разработчику", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
