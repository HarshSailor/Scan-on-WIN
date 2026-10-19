using System.Globalization;
using System.Printing;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ScanPage = ScanOnWin.Core.DocumentPage;

namespace ScanOnWin.App.Printing;

internal sealed class PrintPreviewWindow : Window
{
    private sealed record Choice<T>(string Label, T Value);
    private readonly IReadOnlyList<ScanPage> _pages;
    private readonly int _currentPage;
    private readonly ComboBox _printer = new() { DisplayMemberPath = "Name", MinWidth = 240 };
    private readonly ComboBox _scope = new() { DisplayMemberPath = "Label", MinWidth = 170 };
    private readonly TextBox _range = new() { MinWidth = 150, Text = "1", ToolTip = "Например: 1, 3–5, 8" };
    private readonly TextBox _copies = new() { Width = 75, Text = "1" };
    private readonly ComboBox _paper = new() { DisplayMemberPath = "Label", MinWidth = 130 };
    private readonly ComboBox _orientation = new() { DisplayMemberPath = "Label", MinWidth = 130 };
    private readonly ComboBox _color = new() { DisplayMemberPath = "Label", MinWidth = 150 };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.Firebrick };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly DocumentPageView _pageView = new();
    private readonly Button _previous = new() { Content = "←", ToolTip = "Предыдущая страница" };
    private readonly Button _next = new() { Content = "→", ToolTip = "Следующая страница" };
    private readonly Button _print = new() { Content = "Печать…", MinWidth = 120 };
    private IReadOnlyList<int> _selection = [];
    private PrintTicket _ticket = new();
    private PrintCapabilities? _capabilities;
    private int _previewIndex;
    private bool _updating;
    private int _maxCopies = 9999;
    public string ResultStatus { get; private set; } = "Печать отменена.";

    internal PrintPreviewWindow(IReadOnlyList<ScanPage> pages, int currentPage, IReadOnlyList<PrinterEntry> printers, PrinterMatch match)
    {
        _pages = pages; _currentPage = Math.Clamp(currentPage, 0, pages.Count - 1);
        Title = "Печать — Scan on WIN"; Width = 960; Height = 830; MinWidth = 780; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var root = new Grid { Margin = new Thickness(20) };
        root.SetResourceReference(Panel.BackgroundProperty, "AppBackground");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var options = new StackPanel();
        options.Children.Add(new TextBlock { Text = "Предварительный просмотр печати", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        var printerRow = new DockPanel();
        printerRow.Children.Add(new TextBlock { Text = "Принтер", Width = 80, VerticalAlignment = VerticalAlignment.Center });
        printerRow.Children.Add(_printer); options.Children.Add(printerRow);
        _notice.Text = match.Printer != null ? "Предложена печатающая часть МФУ, с которого получен документ. Можно выбрать другой принтер."
            : match.IsAmbiguous ? "Найдено несколько совпадений с МФУ. Выберите нужный принтер вручную."
            : "Выберите принтер. Однозначное совпадение с устройством сканирования не найдено.";
        options.Children.Add(_notice);
        var scopeRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        scopeRow.Children.Add(Field("Страницы", _scope)); scopeRow.Children.Add(Field("Номера страниц", _range));
        var settingsRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        settingsRow.Children.Add(Field("Копии", _copies)); settingsRow.Children.Add(Field("Бумага", _paper));
        settingsRow.Children.Add(Field("Ориентация", _orientation)); settingsRow.Children.Add(Field("Цвет", _color));
        options.Children.Add(scopeRow); options.Children.Add(settingsRow);
        options.Children.Add(new TextBlock
        {
            Text = "Изображения вписываются в область печати с сохранением пропорций. Затем откроется стандартное окно Windows: выбор принтера и его свойства. В него передаются только выбранные здесь страницы.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 0)
        });
        options.Children.Add(_error); root.Children.Add(options);
        var preview = new Border { Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(14), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        preview.SetResourceReference(Border.BackgroundProperty, "PreviewBackground"); preview.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        preview.Child = new Viewbox { Stretch = Stretch.Uniform, Child = _pageView }; Grid.SetRow(preview, 1); root.Children.Add(preview);
        var footer = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        var close = new Button { Content = "Закрыть", IsCancel = true, Margin = new Thickness(10, 0, 0, 0) };
        close.Click += (_, _) => Close(); actions.Children.Add(_print); actions.Children.Add(close); footer.Children.Add(actions);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        navigation.Children.Add(_previous); navigation.Children.Add(_pageLabel); navigation.Children.Add(_next); footer.Children.Add(navigation);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _print.SetResourceReference(StyleProperty, "PrimaryButton");
        _print.Content = new TextBlock { Text = "Печать…", Foreground = Brushes.White };
        AutomationProperties.SetName(_printer, "Принтер печати"); AutomationProperties.SetName(_scope, "Страницы для печати");
        AutomationProperties.SetName(_range, "Номера страниц для печати"); AutomationProperties.SetName(_copies, "Копии печати");
        AutomationProperties.SetName(_paper, "Бумага для печати"); AutomationProperties.SetName(_orientation, "Ориентация печати"); AutomationProperties.SetName(_color, "Цвет печати");
        _scope.ItemsSource = new[] { new Choice<int>("Все страницы", 0), new Choice<int>($"Текущая ({_currentPage + 1})", 1), new Choice<int>("Выбранные страницы", 2) };
        _scope.SelectedIndex = 0; _range.Text = (_currentPage + 1).ToString(CultureInfo.InvariantCulture);
        _printer.ItemsSource = printers; _printer.SelectedItem = match.Printer == null ? null : printers.FirstOrDefault(p => p.Info == match.Printer);
        _printer.SelectionChanged += (_, _) => LoadPrinter();
        _scope.SelectionChanged += (_, _) => { _previewIndex = 0; RefreshPreview(); };
        _range.TextChanged += (_, _) => { _previewIndex = 0; RefreshPreview(); };
        _copies.TextChanged += (_, _) => RefreshPreview();
        _paper.SelectionChanged += (_, _) => RefreshPreview(); _orientation.SelectionChanged += (_, _) => RefreshPreview(); _color.SelectionChanged += (_, _) => RefreshPreview();
        _previous.Click += (_, _) => { _previewIndex--; RefreshPreview(); }; _next.Click += (_, _) => { _previewIndex++; RefreshPreview(); };
        _print.Click += (_, _) => Print();
        LoadPrinter();
    }

    private static StackPanel Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 5) }); panel.Children.Add(control); return panel;
    }

    private void LoadPrinter()
    {
        _updating = true;
        try
        {
            _capabilities = null; _ticket = new PrintTicket(); _maxCopies = 9999;
            if (_printer.SelectedItem is PrinterEntry printer)
            {
                _ticket = (printer.Queue.UserPrintTicket ?? printer.Queue.DefaultPrintTicket ?? new PrintTicket()).Clone();
                _capabilities = printer.Queue.GetPrintCapabilities(_ticket);
                _maxCopies = Math.Max(1, _capabilities.MaxCopyCount ?? 9999);
            }
            var sizes = _capabilities?.PageMediaSizeCapability.Where(p => p.Width > 0 && p.Height > 0).ToArray() ?? [];
            _paper.ItemsSource = sizes.Select(p => new Choice<PageMediaSize>(PaperLabel(p), p)).ToArray();
            _paper.SelectedIndex = Array.FindIndex(sizes, p => p.PageMediaSizeName == _ticket.PageMediaSize?.PageMediaSizeName);
            if (_paper.SelectedIndex < 0 && sizes.Length > 0) _paper.SelectedIndex = 0;
            var orientations = _capabilities?.PageOrientationCapability.ToArray() ?? [];
            _orientation.ItemsSource = orientations.Select(p => new Choice<PageOrientation>(OrientationLabel(p), p)).ToArray();
            _orientation.SelectedIndex = Array.IndexOf(orientations, _ticket.PageOrientation ?? PageOrientation.Portrait);
            if (_orientation.SelectedIndex < 0 && orientations.Length > 0) _orientation.SelectedIndex = 0;
            var colors = _capabilities?.OutputColorCapability.ToArray() ?? [];
            _color.ItemsSource = colors.Select(p => new Choice<OutputColor>(ColorLabel(p), p)).ToArray();
            _color.SelectedIndex = Array.IndexOf(colors, _ticket.OutputColor ?? OutputColor.Monochrome);
            if (_color.SelectedIndex < 0 && colors.Length > 0) _color.SelectedIndex = 0;
            _paper.IsEnabled = sizes.Length > 0; _orientation.IsEnabled = orientations.Length > 0; _color.IsEnabled = colors.Length > 0;
            _copies.IsEnabled = _printer.SelectedItem != null && _maxCopies > 1;
            _copies.Text = Math.Clamp(_ticket.CopyCount ?? 1, 1, _maxCopies).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception error)
        {
            _capabilities = null;
            _paper.ItemsSource = null; _orientation.ItemsSource = null; _color.ItemsSource = null;
            _paper.IsEnabled = _orientation.IsEnabled = _color.IsEnabled = false;
            _notice.Text = "Драйвер не сообщил параметры принтера. Откройте его свойства в стандартном окне Windows. " + error.Message;
        }
        finally { _updating = false; }
        RefreshPreview();
    }

    private PrintTicket Options()
    {
        if (!int.TryParse(_copies.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var copies) || copies < 1 || copies > _maxCopies)
            throw new FormatException($"Укажите число копий от 1 до {_maxCopies}.");
        var result = _ticket.Clone(); result.CopyCount = copies;
        if (_paper.SelectedItem is Choice<PageMediaSize> paper) result.PageMediaSize = paper.Value;
        if (_orientation.SelectedItem is Choice<PageOrientation> orientation) result.PageOrientation = orientation.Value;
        if (_color.SelectedItem is Choice<OutputColor> color) result.OutputColor = color.Value;
        return result;
    }

    private void RefreshPreview()
    {
        if (_updating) return;
        try
        {
            _range.IsEnabled = _scope.SelectedIndex == 2;
            _selection = _scope.SelectedIndex switch
            {
                1 => [_currentPage], 2 => PageSelection.Parse(_range.Text, _pages.Count), _ => Enumerable.Range(0, _pages.Count).ToArray()
            };
            _previewIndex = Math.Clamp(_previewIndex, 0, _selection.Count - 1);
            var ticket = Options();
            // Capabilities are queried for this ticket, since paper and orientation affect the imageable area.
            var capabilities = _printer.SelectedItem is PrinterEntry printer && _capabilities != null ? printer.Queue.GetPrintCapabilities(ticket) : null;
            var paginator = new ImageDocumentPaginator(_pages, _selection, PrintLayout.From(ticket, capabilities));
            // Render now so missing/invalid images produce an actionable error before submitting a job.
            paginator.GetPage(_previewIndex);
            _pageView.DocumentPaginator = paginator; _pageView.PageNumber = _previewIndex;
            _pageView.Width = paginator.PageSize.Width; _pageView.Height = paginator.PageSize.Height;
            _pageLabel.Text = $"{_previewIndex + 1} / {_selection.Count} · страница {_selection[_previewIndex] + 1}";
            _previous.IsEnabled = _previewIndex > 0; _next.IsEnabled = _previewIndex + 1 < _selection.Count;
            _error.Text = ""; _print.IsEnabled = _printer.SelectedItem != null;
        }
        catch (Exception error)
        {
            _error.Text = error.Message; _print.IsEnabled = false;
            _pageView.DocumentPaginator = null; _pageLabel.Text = "Нет просмотра";
            _previous.IsEnabled = _next.IsEnabled = false;
        }
    }

    private void Print()
    {
        if (_printer.SelectedItem is not PrinterEntry printer) return;
        try
        {
            var ticket = printer.Queue.MergeAndValidatePrintTicket(printer.Queue.DefaultPrintTicket, Options()).ValidatedPrintTicket;
            var dialog = new PrintDialog
            {
                PrintQueue = printer.Queue, PrintTicket = ticket, PageRangeSelection = PageRangeSelection.AllPages,
                MinPage = 1, MaxPage = (uint)_selection.Count, UserPageRangeEnabled = false, CurrentPageEnabled = false, SelectedPagesEnabled = false
            };
            if (dialog.ShowDialog() != true) return;
            var chosenQueue = dialog.PrintQueue ?? throw new InvalidOperationException("Принтер не выбран.");
            var finalTicket = dialog.PrintTicket ?? ticket;
            PrintCapabilities? finalCapabilities;
            try { finalCapabilities = chosenQueue.GetPrintCapabilities(finalTicket); }
            catch (PrintSystemException) { finalCapabilities = null; }
            var paginator = new ImageDocumentPaginator(_pages, _selection, PrintLayout.From(finalTicket, finalCapabilities));
            // Validate all source images before the first page can enter the spooler.
            for (int page = 0; page < paginator.PageCount; page++) paginator.GetPage(page);
            _print.IsEnabled = false;
            dialog.PrintDocument(paginator, "Scan on WIN — отсканированный документ");
            ResultStatus = $"В очередь «{chosenQueue.Name}» передано страниц: {_selection.Count}, копий: {finalTicket.CopyCount ?? 1}.";
            DialogResult = true;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Не удалось передать документ на печать. Проверьте принтер, его подключение и очередь печати.\n\n" + error.Message,
                "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResultStatus = "Ошибка печати: " + error.Message; RefreshPreview();
        }
    }

    private static string PaperLabel(PageMediaSize size)
    {
        var name = size.PageMediaSizeName?.ToString() ?? "Бумага";
        if (name.StartsWith("ISO", StringComparison.Ordinal)) name = name[3..];
        else if (name.StartsWith("NorthAmerica", StringComparison.Ordinal)) name = name[12..];
        return $"{name} ({size.Width * 25.4 / 96:0.#} × {size.Height * 25.4 / 96:0.#} мм)";
    }
    private static string OrientationLabel(PageOrientation value) => value switch
    {
        PageOrientation.Portrait => "Книжная", PageOrientation.Landscape => "Альбомная",
        PageOrientation.ReversePortrait => "Обратная книжная", PageOrientation.ReverseLandscape => "Обратная альбомная", _ => value.ToString()
    };
    private static string ColorLabel(OutputColor value) => value switch
    {
        OutputColor.Color => "Цветной", OutputColor.Monochrome => "Чёрно-белый", OutputColor.Grayscale => "Оттенки серого", _ => value.ToString()
    };
}
