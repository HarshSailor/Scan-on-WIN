using System.Collections.ObjectModel;
using System.Diagnostics;
using NAPS2.Images;
using NAPS2.Scan;
using ScanOnWin.Core;
using Microsoft.Win32;

namespace ScanOnWin.App;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly ScanningRuntime _runtime;
    private readonly ScannerDiscovery _discovery;
    private readonly DocumentManager _document;
    private readonly SettingsService _settingsService;
    private readonly LoggingService _log;
    private readonly DocumentExporter _exporter;
    private readonly Printing.DocumentPrintSource _printSource;
    private readonly List<RelayCommand> _commands = new();
    private CancellationTokenSource? _operation;
    private string? _activeOperation;
    private ScanCaps? _caps;
    private readonly AppSettings _settings;
    private readonly string _acquisitionFolder;
    public IntPtr WindowHandle { get; set; }
    public ObservableCollection<ScannerDevice> Devices { get; } = new();
    public ObservableCollection<PageViewModel> Pages { get; } = new();
    public ObservableCollection<Choice<PaperSource>> Sources { get; } = new();
    public ObservableCollection<int> DpiValues { get; } = new();
    public ObservableCollection<Choice<BitDepth>> Colors { get; } = new();
    public ObservableCollection<Choice<string>> PageSizes { get; } = new();
    public string[] Formats { get; } = ["PDF", "PNG", "JPG", "TXT"];
    public Choice<string>[] Languages { get; } = [new("rus+eng", "Русский + английский"), new("rus", "Русский"), new("eng", "Английский")];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand PrintCommand { get; }
    public event EventHandler? PrintRequested;
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RotateLeftCommand { get; }
    public RelayCommand RotateRightCommand { get; }
    public RelayCommand Rotate180Command { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand AdjustCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand ThemeCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand FitCommand { get; }
    public RelayCommand LogsCommand { get; }
    public string DataRoot { get; }
    public string StartupWarning { get; private set; } = "";
    public bool Unsaved => _document.Unsaved;
    public MainViewModel(string root)
    {
        DataRoot = root; Directory.CreateDirectory(root);
        _settingsService = new SettingsService(root); _settings = _settingsService.Load();
        _log = new LoggingService(Path.Combine(root, "logs"));
        _document = new DocumentManager(Path.Combine(root, "document"));
        try { if (_document.Restore()) _status = "Восстановлен предыдущий документ. Можно продолжить редактирование и сохранение."; }
        catch (Exception e) { _log.Error("recovery", e); StartupWarning = "Не удалось восстановить предыдущий документ. Исходные файлы сохранены в папке данных программы."; }
        _printSource = new Printing.DocumentPrintSource(_document.Pages.Count > 0);
        _runtime = new ScanningRuntime(Path.Combine(root, "temp"));
        _discovery = new ScannerDiscovery(_runtime.Backends, _log);
        var ocr = new OcrService(_runtime.Context, Path.Combine(AppContext.BaseDirectory, "tessdata"));
        _exporter = new DocumentExporter(new Core.PdfExporter(_runtime.Context, ocr), ocr);
        _acquisitionFolder = Path.Combine(root, "acquisition");
        _dpi = _settings.Dpi; _source = Enum.TryParse<PaperSource>(_settings.PaperSource, out var s) ? s : PaperSource.Flatbed;
        _color = Enum.TryParse<BitDepth>(_settings.Color, out var c) ? c : BitDepth.Color;
        _pageSize = _settings.PageSize; _format = Formats.Contains(_settings.Format) ? _settings.Format : "PDF";
        _quality = Math.Clamp(_settings.Quality, 1, 100); _ocr = _settings.Ocr;
        _language = Languages.Any(x => x.Value == _settings.OcrLanguage) ? _settings.OcrLanguage : "rus+eng";
        _outputFolder = _settings.OutputFolder; _openFolder = _settings.OpenFolder;
        _compress = _settings.CompressPdf; _dark = _settings.DarkTheme; _deskew = _settings.AutoDeskew;
        _nativeUi = _settings.NativeUi; _customWidth = _settings.CustomWidth; _customHeight = _settings.CustomHeight;
        RefreshCommand = Command(() => _ = Discover(), () => !IsBusy);
        ScanCommand = Command(() => _ = Acquire(false), () => CanScan);
        PreviewCommand = Command(() => _ = Acquire(true), () => CanScan && Source == PaperSource.Flatbed);
        SaveCommand = Command(() => _ = Save(), () => !IsBusy && Pages.Count > 0);
        PrintCommand = Command(() => PrintRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && Pages.Count > 0);
        CancelCommand = Command(() => { _operation?.Cancel(); Status = "Отмена… Ожидание завершения операции драйвера."; }, () => IsBusy);
        BrowseCommand = Command(Browse, () => !IsBusy);
        NewCommand = Command(NewDocument, () => !IsBusy && Pages.Count > 0);
        DeleteCommand = Command(Delete, () => CanEdit);
        RotateLeftCommand = Command(() => _ = Edit((b, p) => ScanProcessing.Rotate(b, p, 270)), () => CanEdit);
        RotateRightCommand = Command(() => _ = Edit((b, p) => ScanProcessing.Rotate(b, p, 90)), () => CanEdit);
        Rotate180Command = Command(() => _ = Edit((b, p) => ScanProcessing.Rotate(b, p, 180)), () => CanEdit);
        UndoCommand = Command(() => _ = Run("undo", _ => { _document.Undo(SelectedPage!.Page); RefreshPages(SelectedPage.Page.Id); return Task.CompletedTask; }), () => CanEdit && SelectedPage!.Page.History.Count > 0);
        AdjustCommand = Command(() => _ = Edit((b, p) => ScanProcessing.Adjust(b, p, Brightness, Contrast)), () => CanEdit && (Brightness != 0 || Contrast != 0));
        MoveUpCommand = Command(() => MoveSelected(-1), () => CanEdit && Pages.IndexOf(SelectedPage!) > 0);
        MoveDownCommand = Command(() => MoveSelected(1), () => CanEdit && Pages.IndexOf(SelectedPage!) < Pages.Count - 1);
        ThemeCommand = Command(() => { DarkTheme = !DarkTheme; SaveSettings(); });
        ZoomInCommand = Command(() => Zoom = Math.Min(4, Zoom * 1.2), () => SelectedPage != null);
        ZoomOutCommand = Command(() => Zoom = Math.Max(0.2, Zoom / 1.2), () => SelectedPage != null);
        FitCommand = Command(() => Zoom = 1);
        LogsCommand = Command(() => { Directory.CreateDirectory(Path.Combine(root, "logs")); OpenDirectory(Path.Combine(root, "logs")); });
        RefreshPages();
    }
    private RelayCommand Command(Action action, Func<bool>? can = null) { var command = new RelayCommand(action, can); _commands.Add(command); return command; }
    private void RefreshCommands() { foreach (var command in _commands) command.Refresh(); Changed(nameof(CanEdit)); Changed(nameof(CanScan)); Changed(nameof(UiEnabled)); Changed(nameof(NativeUiEnabled)); Changed(nameof(ManualSettingsEnabled)); Changed(nameof(DocumentInfo)); }
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); RefreshCommands(); Changed(nameof(IsIndeterminate)); } }
    public bool UiEnabled => !IsBusy;
    public bool CanChooseSaveFormat => _activeOperation != "export";
    public bool CanEdit => !IsBusy && SelectedPage != null;
    public bool CanScan => !IsBusy && SelectedDevice != null && (_caps != null || NativeUi) && (Sources.Count > 0 || NativeUi);
    public bool ManualSettingsEnabled => !IsBusy && !NativeUi && _caps != null;
    public bool NativeUiAvailable => SelectedDevice?.Device.Driver is Driver.Wia or Driver.Twain;
    public bool NativeUiEnabled => UiEnabled && NativeUiAvailable;
    private int _progress = -1;
    public int Progress { get => Math.Max(0, _progress); private set { _progress = value; Changed(); Changed(nameof(IsIndeterminate)); } }
    public bool IsIndeterminate => IsBusy && _progress < 0;
    private string _status = "Готово к работе. Выберите сканер и положите документ.";
    public string Status { get => _status; set => Set(ref _status, value); }
    private string _discoveryNote = "Поиск подключённых устройств…";
    public string DiscoveryNote { get => _discoveryNote; set => Set(ref _discoveryNote, value); }
    private string _capabilitiesNote = "Выберите устройство, чтобы получить доступные настройки.";
    public string CapabilitiesNote { get => _capabilitiesNote; set => Set(ref _capabilitiesNote, value); }
    private ScannerDevice? _selectedDevice;
    public ScannerDevice? SelectedDevice { get => _selectedDevice; set { if (Set(ref _selectedDevice, value)) { _caps = null; Changed(nameof(NativeUiAvailable)); _ = Run("capabilities", LoadCapabilities); } } }
    private PageViewModel? _selectedPage;
    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (!Set(ref _selectedPage, value)) return;
            PreviewImage = value == null ? null : PageViewModel.LoadBitmap(value.Page.CurrentPath, Math.Min(2200, value.Page.PixelWidth));
            Brightness = 0; Contrast = 0; RefreshCommands(); Changed(nameof(HasPage)); Changed(nameof(DocumentInfo));
        }
    }
    public bool HasPage => SelectedPage != null;
    private System.Windows.Media.Imaging.BitmapImage? _previewImage;
    public System.Windows.Media.Imaging.BitmapImage? PreviewImage { get => _previewImage; private set => Set(ref _previewImage, value); }
    public string DocumentInfo => SelectedPage == null ? $"{Pages.Count} стр. в документе" : $"Страница {SelectedPage.Number} из {Pages.Count}  ·  {SelectedPage.Page.PixelWidth} × {SelectedPage.Page.PixelHeight} px  ·  {SelectedPage.Page.Dpi:0} DPI";
    private PaperSource _source;
    public PaperSource Source { get => _source; set { if (Set(ref _source, value)) { UpdateSourceCapabilities(); RefreshCommands(); } } }
    private int _dpi;
    public int Dpi { get => _dpi; set => Set(ref _dpi, value); }
    private BitDepth _color;
    public BitDepth Color { get => _color; set => Set(ref _color, value); }
    private string _pageSize;
    public string PageSize { get => _pageSize; set { Set(ref _pageSize, value); Changed(nameof(IsCustomSize)); } }
    public bool IsCustomSize => PageSize == "Custom";
    private double _customWidth, _customHeight;
    public double CustomWidth { get => _customWidth; set => Set(ref _customWidth, value); }
    public double CustomHeight { get => _customHeight; set => Set(ref _customHeight, value); }
    private string _format;
    public string Format
    {
        get => _format;
        set
        {
            if (!Formats.Contains(value) || !Set(ref _format, value)) return;
            Changed(nameof(OcrSettingsEnabled)); Changed(nameof(QualityEnabled));
            SaveSettings();
        }
    }
    private int _quality;
    public int Quality { get => _quality; set => Set(ref _quality, value); }
    public bool QualityEnabled => Format == "JPG" || (Format == "PDF" && CompressPdf);
    private bool _compress;
    public bool CompressPdf { get => _compress; set { Set(ref _compress, value); Changed(nameof(QualityEnabled)); } }
    private bool _ocr;
    public bool Ocr { get => _ocr; set => Set(ref _ocr, value); }
    public bool OcrSettingsEnabled => Format is "PDF" or "TXT";
    private string _language;
    public string Language { get => _language; set => Set(ref _language, value); }
    private string _outputFolder;
    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }
    private string _fileName = "Scan_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    public string FileName { get => _fileName; set => Set(ref _fileName, value); }
    private bool _openFolder;
    public bool OpenFolder { get => _openFolder; set => Set(ref _openFolder, value); }
    private bool _dark;
    public bool DarkTheme { get => _dark; set { Set(ref _dark, value); App.ApplyTheme(value); } }
    private bool _deskew;
    public bool AutoDeskew { get => _deskew; set => Set(ref _deskew, value); }
    private bool _nativeUi;
    public bool NativeUi { get => _nativeUi; set { Set(ref _nativeUi, value); RefreshCommands(); } }
    private double _zoom = 1;
    public double Zoom { get => _zoom; set { Set(ref _zoom, value); Changed(nameof(ZoomLabel)); } }
    public string ZoomLabel => $"{Zoom * 100:0}%";
    private double _brightness, _contrast;
    public double Brightness { get => _brightness; set { Set(ref _brightness, value); RefreshCommands(); } }
    public double Contrast { get => _contrast; set { Set(ref _contrast, value); RefreshCommands(); } }

    public async Task Initialize()
    {
        App.ApplyTheme(DarkTheme);
        if (StartupWarning.Length > 0) MessageBox.Show(StartupWarning, "Восстановление", MessageBoxButton.OK, MessageBoxImage.Information);
        // Salvage completed acquisitions left behind if the UI stopped between acquisition and manifest update.
        if (Directory.Exists(_acquisitionFolder))
            foreach (var path in Directory.GetFiles(_acquisitionFolder, "*.png"))
                try { _document.Add(path); _printSource.MarkUnknown(); File.Delete(path); } catch (Exception e) { _log.Error("acquisition-recovery", e); }
        RefreshPages();
        await Discover();
    }
    private async Task Discover() => await Run("discovery", async token =>
    {
        var previous = SelectedDevice?.Key ?? _settings.DeviceKey;
        var report = await Task.Run(() => _discovery.Discover(new Progress<string>(message => Application.Current.Dispatcher.Invoke(() => Status = message)), token));
        Devices.Clear(); foreach (var device in report.Devices) Devices.Add(device);
        _selectedDevice = Devices.FirstOrDefault(d => d.Key == previous) ?? Devices.FirstOrDefault();
        Changed(nameof(SelectedDevice)); Changed(nameof(NativeUiAvailable));
        DiscoveryNote = Devices.Count == 0
            ? "Устройства не найдены. Проверьте питание и подключение. Для USB установите WIA/TWAIN-драйвер производителя; для сети включите AirScan/eSCL и подключитесь к той же сети."
            : $"Найдено интерфейсов: {Devices.Count}. Для одного сканера могут быть доступны разные драйверы.";
        if (report.FailedBackends.Count > 0) DiscoveryNote += " Не удалось проверить: " + string.Join(", ", report.FailedBackends) + ".";
        await LoadCapabilities(token);
        Status = Devices.Count == 0 ? "Сканер не выбран. Документ можно восстановить, редактировать и сохранить." : "Готово к сканированию.";
    });
    private async Task LoadCapabilities(CancellationToken token)
    {
        _caps = null; Sources.Clear(); DpiValues.Clear(); Colors.Clear(); PageSizes.Clear();
        if (SelectedDevice == null) { CapabilitiesNote = "Нет подключённого сканера."; RefreshCommands(); return; }
        Status = "Проверка возможностей устройства…";
        try
        {
            var device = SelectedDevice;
            _caps = await Task.Run(() => device.Backend.Capabilities(device, token));
            foreach (var source in ScanCapabilities.Sources(_caps)) Sources.Add(new(source, SourceLabel(source)));
            if (Sources.Count > 0 && !Sources.Any(x => x.Value == Source)) _source = Sources[0].Value;
            Changed(nameof(Source));
            if (!NativeUiAvailable) NativeUi = false;
            if (Sources.Count == 0 && NativeUiAvailable) NativeUi = true;
            CapabilitiesNote = Sources.Count == 0 ? "Драйвер не сообщил параметры. Используйте окно драйвера." : "Список параметров получен от устройства. Недоступные варианты скрыты.";
            UpdateSourceCapabilities();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            _log.Error("capabilities", e); CapabilitiesNote = FriendlyError.Describe(e, "capabilities");
            if (NativeUiAvailable) { NativeUi = true; CapabilitiesNote += " Параметры можно выбрать в окне драйвера."; }
        }
        RefreshCommands();
    }
    private void UpdateSourceCapabilities()
    {
        if (_caps == null) return;
        var source = ScanCapabilities.ForSource(_caps, Source);
        DpiValues.Clear(); foreach (var dpi in source?.DpiCaps?.Values ?? []) if (dpi is > 0 and <= 9600) DpiValues.Add(dpi);
        if (DpiValues.Count == 0) foreach (var dpi in new[] { 75, 150, 200, 300, 600 }) DpiValues.Add(dpi);
        if (!DpiValues.Contains(Dpi)) Dpi = DpiValues.OrderBy(d => Math.Abs(d - Dpi)).First();
        Colors.Clear(); var colors = ScanCapabilities.Colors(source);
        if (source?.BitDepthCaps == null) colors = [BitDepth.Color, BitDepth.Grayscale, BitDepth.BlackAndWhite];
        foreach (var color in colors) Colors.Add(new(color, ColorLabel(color)));
        if (Colors.Count > 0 && !Colors.Any(x => x.Value == Color)) Color = Colors[0].Value;
        PageSizes.Clear();
        foreach (var (key, size) in new[] { ("A4", NAPS2.Images.PageSize.A4), ("A5", NAPS2.Images.PageSize.A5), ("Letter", NAPS2.Images.PageSize.Letter) })
            if (source?.PageSizeCaps == null || source.PageSizeCaps.Fits(size)) PageSizes.Add(new(key, key));
        PageSizes.Add(new("Auto", "Автоматический / вся область"));
        if (source?.PageSizeCaps?.ScanArea != null) PageSizes.Add(new("Custom", "Пользовательский, мм"));
        if (!PageSizes.Any(x => x.Value == PageSize)) PageSize = "Auto";
        if (source?.DpiCaps?.Values == null || source.BitDepthCaps == null) CapabilitiesNote = "Часть параметров драйвер не сообщает. Показаны стандартные значения; устройство проверит их при сканировании.";
    }
    private NAPS2.Images.PageSize? ResolvePageSize() => PageSize switch
    {
        "A4" => NAPS2.Images.PageSize.A4, "A5" => NAPS2.Images.PageSize.A5, "Letter" => NAPS2.Images.PageSize.Letter,
        "Custom" when CustomWidth is > 0 and <= 2000 && CustomHeight is > 0 and <= 10000 => new((decimal)CustomWidth, (decimal)CustomHeight, PageSizeUnit.Millimetre),
        "Custom" => throw new ArgumentException("Page size"), _ => null
    };
    private async Task Acquire(bool preview) => await Run("scan", async token =>
    {
        var device = SelectedDevice!;
        var dpi = preview ? DpiValues.Where(x => x <= 150).DefaultIfEmpty(Dpi).Max() : Dpi;
        var request = new ScanRequest(Source, dpi, Color, ResolvePageSize(), NativeUi, AutoDeskew, WindowHandle);
        Status = preview ? "Предварительное сканирование… Страница будет добавлена в документ." : "Сканирование… Полученные страницы добавляются в документ.";
        SaveSettings(); var count = 0;
        await Task.Run(async () =>
        {
            await foreach (var path in device.Backend.Scan(device, request, _acquisitionFolder, token))
            {
                var page = _document.Add(path, dpi); File.Delete(path); count++;
                await Application.Current.Dispatcher.InvokeAsync(() => { _printSource.PageAdded(device.Key, device.Device.Name, device.Device.ConnectionUri ?? device.Device.ID); var view = new PageViewModel(page, Pages.Count + 1); Pages.Add(view); SelectedPage = view; Status = $"Получено страниц: {count}. Сканирование…"; });
            }
        });
        Status = $"Сканирование завершено. Добавлено страниц: {count}.";
    });
    private async Task Save() => await Run("export", async token =>
    {
        var options = new ExportOptions(OutputFolder, FileName, Format, Quality, CompressPdf, Ocr, Language);
        var paths = DocumentExporter.TargetPaths(Pages.Count, options);
        if (paths.Any(File.Exists)) { Status = "Файл с таким именем уже существует. Измените имя документа."; return; }
        Progress = 0; Status = Format == "TXT" || Format == "PDF" && Ocr ? "Распознавание и сохранение документа…" : "Сохранение документа…";
        SaveSettings();
        var progress = new Progress<int>(value => Progress = value);
        var result = await Task.Run(() => _exporter.Export(_document.Pages.ToArray(), options, progress, token));
        _document.MarkSaved(); Status = $"Сохранено файлов: {result.Count}. {Path.GetFileName(result[0])}";
        FileName = "Scan_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        if (OpenFolder) OpenDirectory(OutputFolder);
    });
    private async Task Edit(Action<System.Drawing.Bitmap, string> edit) => await Run("edit", async _ =>
    {
        var page = SelectedPage!.Page;
        await Task.Run(() => _document.Replace(page, edit)); RefreshPages(page.Id); Status = "Изменения применены. Можно отменить последнюю правку.";
    });
    public Task Crop(System.Drawing.Rectangle rectangle) => Edit((bitmap, path) => ScanProcessing.Crop(bitmap, path, rectangle));
    public Task Print(Window owner) => Run("print", _ =>
    {
        Status = Printing.PrintService.Show(owner, _document.Pages.ToArray(),
            SelectedPage == null ? 0 : Pages.IndexOf(SelectedPage), _printSource.ScannerName, _printSource.ScannerId);
        return Task.CompletedTask;
    });
    private void Delete()
    {
        if (SelectedPage == null) return;
        if (MessageBox.Show("Удалить выбранную страницу из документа?", "Удаление страницы", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _ = Run("delete", _ => { var index = Pages.IndexOf(SelectedPage); _document.Delete(SelectedPage.Page); if (_document.Pages.Count == 0) _printSource.Reset(); RefreshPages(); SelectedPage = Pages.ElementAtOrDefault(Math.Min(index, Pages.Count - 1)); Status = "Страница удалена из документа."; return Task.CompletedTask; });
    }
    private void NewDocument()
    {
        if (MessageBox.Show("Начать новый документ? Текущие страницы будут удалены из рабочей области. Сохранённые файлы останутся на диске.", "Новый документ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _ = Run("new-document", _ => { _document.Clear(); _printSource.Reset(); RefreshPages(); FileName = "Scan_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"); Status = "Создан новый документ."; return Task.CompletedTask; });
    }
    private void MoveSelected(int delta) { if (SelectedPage != null) Reorder(Pages.IndexOf(SelectedPage), Pages.IndexOf(SelectedPage) + delta); }
    public void Reorder(int from, int to)
    {
        if (IsBusy) return;
        var id = SelectedPage?.Page.Id;
        _ = Run("reorder", _ => { _document.Move(from, to); RefreshPages(id); return Task.CompletedTask; });
    }
    private void RefreshPages(Guid? selectedId = null)
    {
        selectedId ??= SelectedPage?.Page.Id;
        Pages.Clear(); foreach (var page in _document.Pages) Pages.Add(new PageViewModel(page, Pages.Count + 1));
        SelectedPage = Pages.FirstOrDefault(p => p.Page.Id == selectedId) ?? Pages.FirstOrDefault(); RefreshCommands();
    }
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Папка для сохранения документов", InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog() == true) OutputFolder = dialog.FolderName;
    }
    private void OpenDirectory(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", Path.GetFullPath(path)) { UseShellExecute = true }); }
        catch (Exception e) { _log.Error("open-folder", e); Status = "Документ сохранён, но открыть папку не удалось."; }
    }
    public void SaveSettings()
    {
        _settings.DeviceKey = SelectedDevice?.Key ?? _settings.DeviceKey;
        _settings.Dpi = Dpi; _settings.PaperSource = Source.ToString(); _settings.Color = Color.ToString(); _settings.PageSize = PageSize;
        _settings.CustomWidth = CustomWidth; _settings.CustomHeight = CustomHeight; _settings.Format = Format; _settings.Quality = Quality;
        _settings.CompressPdf = CompressPdf; _settings.Ocr = Ocr; _settings.OcrLanguage = Language; _settings.OutputFolder = OutputFolder;
        _settings.OpenFolder = OpenFolder; _settings.DarkTheme = DarkTheme; _settings.AutoDeskew = AutoDeskew; _settings.NativeUi = NativeUi;
        try { _settingsService.Save(_settings); } catch (Exception e) { _log.Error("settings", e); }
    }
    private async Task Run(string operation, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        _activeOperation = operation; Changed(nameof(CanChooseSaveFormat));
        IsBusy = true; Progress = -1; _operation = new CancellationTokenSource();
        try { await action(_operation.Token); }
        catch (Exception error) { _log.Error(operation, error); Status = FriendlyError.Describe(error, operation); }
        finally { _operation.Dispose(); _operation = null; IsBusy = false; Progress = 0; _activeOperation = null; Changed(nameof(CanChooseSaveFormat)); }
    }
    public void Dispose() { SaveSettings(); _operation?.Cancel(); _runtime.Dispose(); }
    private static string SourceLabel(PaperSource source) => source switch { PaperSource.Flatbed => "Стекло сканера", PaperSource.Feeder => "Автоподатчик (ADF)", PaperSource.Duplex => "Двусторонний ADF", _ => "Автоматический источник" };
    private static string ColorLabel(BitDepth color) => color switch { BitDepth.Color => "Цветной", BitDepth.Grayscale => "Оттенки серого", _ => "Чёрно-белый" };
}
