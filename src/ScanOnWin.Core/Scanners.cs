using NAPS2.Scan;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using System.Runtime.CompilerServices;

namespace ScanOnWin.Core;

public sealed record ScannerDevice(ScanDevice Device, IScannerBackend Backend)
{
    public string Key => $"{Backend.Id}:{Device.ID}";
    public string DisplayName => $"{Device.Name}  ·  {Backend.Label}";
}
public sealed record ScanRequest(PaperSource Source, int Dpi, BitDepth Color, PageSize? Size,
    bool NativeUi, bool Deskew, IntPtr Parent);

public interface IScannerBackend
{
    string Id { get; }
    string Label { get; }
    Task<IReadOnlyList<ScannerDevice>> Discover(CancellationToken cancellationToken);
    Task<ScanCaps> Capabilities(ScannerDevice device, CancellationToken cancellationToken);
    IAsyncEnumerable<string> Scan(ScannerDevice device, ScanRequest request, string outputFolder, CancellationToken cancellationToken);
}

public sealed class ScanningRuntime : IDisposable
{
    public ScanningContext Context { get; }
    public IReadOnlyList<IScannerBackend> Backends { get; }
    public ScanningRuntime(string tempFolder, string? nativeWorkerPath = null)
    {
        Directory.CreateDirectory(tempFolder);
        Context = new ScanningContext(new GdiImageContext())
        {
            TempFolderPath = tempFolder,
            FileStorageManager = new FileStorageManager(tempFolder)
        };
        Context.SetUpWin32Worker();
        Backends = [ new WiaScanner(Context), new TwainScanner(Context, TwainDsm.New),
            new TwainScanner(Context, TwainDsm.Old), new Twain64Scanner(nativeWorkerPath ?? Path.Combine(AppContext.BaseDirectory, "ScanOnWIN.exe")), new NetworkScanner(Context) ];
    }
    public void Dispose() => Context.Dispose();
}

public abstract class SdkScanner : IScannerBackend
{
    private readonly ScanController _controller;
    protected SdkScanner(ScanningContext context) => _controller = new ScanController(context);
    public abstract string Id { get; }
    public abstract string Label { get; }
    protected abstract ScanOptions CreateOptions();
    public async Task<IReadOnlyList<ScannerDevice>> Discover(CancellationToken cancellationToken)
    {
        var devices = new List<ScannerDevice>();
        await foreach (var device in _controller.GetDevices(CreateOptions(), cancellationToken))
            devices.Add(new ScannerDevice(device, this));
        cancellationToken.ThrowIfCancellationRequested();
        return devices;
    }
    public Task<ScanCaps> Capabilities(ScannerDevice device, CancellationToken cancellationToken)
    {
        var options = CreateOptions(); options.Device = device.Device;
        return _controller.GetCaps(options, cancellationToken);
    }
    public async IAsyncEnumerable<string> Scan(ScannerDevice device, ScanRequest request, string outputFolder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = CreateOptions();
        options.Device = device.Device; options.PaperSource = request.Source; options.Dpi = request.Dpi;
        options.BitDepth = request.Color; options.PageSize = request.Size; options.UseNativeUI = request.NativeUi;
        options.DialogParent = request.Parent; options.MaxQuality = true; options.AutoDeskew = request.Deskew;
        // GetCaps opens the device, checks availability, and rejects unsupported options before acquisition.
        if (!request.NativeUi)
            ScanCapabilities.Validate(await Capabilities(device, cancellationToken), request);
        Directory.CreateDirectory(outputFolder);
        await foreach (var image in _controller.Scan(options, cancellationToken))
        {
            string path = Path.Combine(outputFolder, Guid.NewGuid().ToString("N") + ".png");
            using (image) image.Save(path);
            // A completed acquisition is delivered even if cancellation arrived just after this page.
            yield return path;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}

public sealed class WiaScanner(ScanningContext context) : SdkScanner(context)
{
    public override string Id => "wia";
    public override string Label => "WIA";
    protected override ScanOptions CreateOptions() => new() { Driver = Driver.Wia };
}
public sealed class TwainScanner(ScanningContext context, TwainDsm dsm) : SdkScanner(context)
{
    public override string Id => "twain-" + dsm;
    public override string Label => dsm switch { TwainDsm.NewX64 => "TWAIN 64-bit", TwainDsm.Old => "TWAIN 32-bit · legacy", _ => "TWAIN 32-bit" };
    protected override ScanOptions CreateOptions() => new() { Driver = Driver.Twain, TwainOptions = new TwainOptions { Dsm = dsm } };
}
public sealed class NetworkScanner(ScanningContext context) : SdkScanner(context)
{
    public override string Id => "escl";
    public override string Label => "eSCL / AirScan";
    protected override ScanOptions CreateOptions() => new() { Driver = Driver.Escl, EsclOptions = new EsclOptions { SearchTimeout = 4000 } };
}

public sealed record DiscoveryReport(IReadOnlyList<ScannerDevice> Devices, IReadOnlyList<string> FailedBackends);
public sealed class ScannerDiscovery(IEnumerable<IScannerBackend> backends, LoggingService log)
{
    public async Task<DiscoveryReport> Discover(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var devices = new List<ScannerDevice>(); var failed = new List<string>();
        foreach (var backend in backends)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Поиск устройств: " + backend.Label);
            try { devices.AddRange(await backend.Discover(cancellationToken)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { log.Error("discovery-" + backend.Id, error); failed.Add(backend.Label); }
        }
        return new(devices.DistinctBy(d => d.Key).ToList(), failed);
    }
}

public static class ScanCapabilities
{
    public static List<PaperSource> Sources(ScanCaps caps)
    {
        var result = new List<PaperSource>(); var s = caps.PaperSourceCaps;
        if (s?.SupportsFlatbed == true) result.Add(PaperSource.Flatbed);
        if (s?.SupportsFeeder == true) result.Add(PaperSource.Feeder);
        if (s?.SupportsDuplex == true) result.Add(PaperSource.Duplex);
        if (s?.CanCheckIfFeederHasPaper == true) result.Add(PaperSource.Auto);
        return result;
    }
    public static PerSourceCaps? ForSource(ScanCaps caps, PaperSource source) => source switch
    {
        PaperSource.Flatbed => caps.FlatbedCaps, PaperSource.Feeder => caps.FeederCaps, PaperSource.Duplex => caps.DuplexCaps,
        _ => PerSourceCaps.UnionAll(new[] { caps.FlatbedCaps, caps.FeederCaps, caps.DuplexCaps }.OfType<PerSourceCaps>())
    };
    public static List<BitDepth> Colors(PerSourceCaps? caps)
    {
        var result = new List<BitDepth>(); var c = caps?.BitDepthCaps;
        if (c?.SupportsColor == true) result.Add(BitDepth.Color);
        if (c?.SupportsGrayscale == true) result.Add(BitDepth.Grayscale);
        if (c?.SupportsBlackAndWhite == true) result.Add(BitDepth.BlackAndWhite);
        return result;
    }
    public static void Validate(ScanCaps caps, ScanRequest request)
    {
        var supported = Sources(caps);
        if (supported.Count == 0 || !supported.Contains(request.Source)) throw new NotSupportedException("Paper source");
        var source = ForSource(caps, request.Source);
        if (source?.DpiCaps?.Values is { } values && !values.Contains(request.Dpi)) throw new NotSupportedException("DPI");
        var colors = Colors(source);
        if (colors.Count > 0 && !colors.Contains(request.Color)) throw new NotSupportedException("Color");
        if (request.Size != null && source?.PageSizeCaps is { } area && !area.Fits(request.Size)) throw new NotSupportedException("Page size");
    }
}
