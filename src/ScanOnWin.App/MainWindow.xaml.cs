using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScanOnWin.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _cropping;
    private Point? _cropStart;
    private Point _dragStart;
    private PageViewModel? _dragPage;
    private readonly bool _initializeOnLoad;
    public MainWindow(MainViewModel vm, bool initializeOnLoad = true)
    {
        _initializeOnLoad = initializeOnLoad;
        InitializeComponent(); _vm = vm; DataContext = vm;
        vm.PrintRequested += (_, _) => _ = vm.Print(this);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.SelectedPage) or nameof(vm.PreviewImage) or nameof(vm.Zoom))
            { CancelCrop(); Dispatcher.BeginInvoke(UpdatePreviewSize); }
            if (e.PropertyName == nameof(vm.IsBusy) && vm.IsBusy) CancelCrop();
        };
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e) { if (_initializeOnLoad) await _vm.Initialize(); UpdatePreviewSize(); }
    private void WindowSourceInitialized(object? sender, EventArgs e) => _vm.WindowHandle = new WindowInteropHelper(this).Handle;
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsBusy) { e.Cancel = true; _vm.CancelCommand.Execute(null); _vm.Status = "Завершите отмену операции, затем закройте окно. Полученные страницы сохраняются."; return; }
        _vm.Dispose();
    }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewSize();
    private void UpdatePreviewSize()
    {
        if (_vm == null) return;
        EmptyState.Visibility = _vm.HasPage ? Visibility.Collapsed : Visibility.Visible;
        if (_vm.SelectedPage == null) return;
        var page = _vm.SelectedPage.Page;
        var width = Math.Max(100, PreviewScroll.ActualWidth - 66);
        var height = Math.Max(100, PreviewScroll.ActualHeight - 66);
        var ratio = Math.Min(width / page.PixelWidth, height / page.PixelHeight) * _vm.Zoom;
        PreviewSurface.Width = page.PixelWidth * ratio; PreviewSurface.Height = page.PixelHeight * ratio;
    }
    private void CropClick(object sender, RoutedEventArgs e)
    {
        if (_cropping) { CancelCrop(); return; }
        _cropping = true; CropCanvas.IsHitTestVisible = true; CropCanvas.Cursor = Cursors.Cross;
        CropHint.Visibility = Visibility.Visible; CropButton.Content = "Отменить обрезку";
    }
    private void CancelCropClick(object sender, RoutedEventArgs e) => CancelCrop();
    private void CancelCrop()
    {
        _cropping = false; _cropStart = null; CropCanvas.ReleaseMouseCapture(); CropCanvas.IsHitTestVisible = false;
        CropHint.Visibility = Visibility.Collapsed; CropRectangle.Visibility = Visibility.Collapsed;
        CropButton.Content = "Обрезать"; ApplyCropButton.IsEnabled = false;
    }
    private Point Bounded(Point p) => new(Math.Clamp(p.X, 0, PreviewSurface.ActualWidth), Math.Clamp(p.Y, 0, PreviewSurface.ActualHeight));
    private void CropStart(object sender, MouseButtonEventArgs e)
    {
        if (!_cropping) return;
        _cropStart = Bounded(e.GetPosition(CropCanvas)); CropCanvas.CaptureMouse();
        Canvas.SetLeft(CropRectangle, _cropStart.Value.X); Canvas.SetTop(CropRectangle, _cropStart.Value.Y);
        CropRectangle.Width = 0; CropRectangle.Height = 0; CropRectangle.Visibility = Visibility.Visible; e.Handled = true;
    }
    private void CropMove(object sender, MouseEventArgs e)
    {
        if (!_cropping || _cropStart == null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = Bounded(e.GetPosition(CropCanvas)); var start = _cropStart.Value;
        Canvas.SetLeft(CropRectangle, Math.Min(start.X, current.X)); Canvas.SetTop(CropRectangle, Math.Min(start.Y, current.Y));
        CropRectangle.Width = Math.Abs(current.X - start.X); CropRectangle.Height = Math.Abs(current.Y - start.Y);
        ApplyCropButton.IsEnabled = CropRectangle.Width > 3 && CropRectangle.Height > 3;
    }
    private void CropEnd(object sender, MouseButtonEventArgs e) { CropCanvas.ReleaseMouseCapture(); _cropStart = null; }
    private async void ApplyCropClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPage == null || !ApplyCropButton.IsEnabled) return;
        var page = _vm.SelectedPage.Page;
        var scaleX = page.PixelWidth / PreviewSurface.ActualWidth; var scaleY = page.PixelHeight / PreviewSurface.ActualHeight;
        var rectangle = new System.Drawing.Rectangle((int)(Canvas.GetLeft(CropRectangle) * scaleX), (int)(Canvas.GetTop(CropRectangle) * scaleY), (int)(CropRectangle.Width * scaleX), (int)(CropRectangle.Height * scaleY));
        CancelCrop(); await _vm.Crop(rectangle);
    }
    private void PageDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PagesList); _dragPage = FindItem(e.OriginalSource as DependencyObject)?.DataContext as PageViewModel;
    }
    private void PageDragMove(object sender, MouseEventArgs e)
    {
        if (_dragPage == null || e.LeftButton != MouseButtonState.Pressed || _vm.IsBusy) return;
        var position = e.GetPosition(PagesList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(PagesList, new DataObject(typeof(PageViewModel), _dragPage), DragDropEffects.Move); _dragPage = null;
    }
    private void PageDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageViewModel)) is not PageViewModel page || _vm.IsBusy) return;
        var target = FindItem(e.OriginalSource as DependencyObject)?.DataContext as PageViewModel;
        _vm.Reorder(_vm.Pages.IndexOf(page), target == null ? _vm.Pages.Count - 1 : _vm.Pages.IndexOf(target)); e.Handled = true;
    }
    private static ListBoxItem? FindItem(DependencyObject? element)
    {
        while (element != null && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element);
        return element as ListBoxItem;
    }
    private void DonationClick(object sender, RoutedEventArgs e) => new DonationWindow { Owner = this }.ShowDialog();
    private void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(
        "Scan on WIN 1.0.1\nБесплатное локальное сканирование документов.\n\nWIA, TWAIN 32/64-bit и eSCL / AirScan.\nДля USB-устройств нужен драйвер производителя.\nOCR: Tesseract, русский и английский.\n\nNAPS2 SDK © NAPS2 Contributors, LGPL-2.1-or-later.\nЛицензии находятся в папке программы.\nДиагностика и восстановление: " + _vm.DataRoot,
        "О программе Scan on WIN", MessageBoxButton.OK, MessageBoxImage.Information);
}
