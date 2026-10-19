using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Scan;

namespace ScanOnWin.Core;

public sealed class ScannerBackendException(string errorType, int code) : Exception
{
    public string ErrorType { get; } = errorType;
    public int Code { get; } = code;
}
public sealed class Twain64Scanner(string executable) : IScannerBackend
{
    public string Id => "twain-NewX64";
    public string Label => "TWAIN 64-bit";
    public async Task<IReadOnlyList<ScannerDevice>> Discover(CancellationToken cancellationToken)
    {
        var devices = new List<ScannerDevice>();
        await foreach (var response in Exchange(new() { Command = "discover" }, cancellationToken, TimeSpan.FromSeconds(20)))
            if (response.Kind == "device") devices.Add(new(new ScanDevice(Driver.Twain, response.DeviceId!, response.Name!), this));
        return devices;
    }
    public async Task<ScanCaps> Capabilities(ScannerDevice device, CancellationToken cancellationToken)
    {
        await foreach (var response in Exchange(new() { Command = "caps", DeviceId = device.Device.ID, Name = device.Device.Name }, cancellationToken, TimeSpan.FromSeconds(30)))
            if (response.Caps != null) return response.Caps.ToCaps();
        throw new IOException("No capabilities response");
    }
    public async IAsyncEnumerable<string> Scan(ScannerDevice device, ScanRequest request, string outputFolder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var message = new Twain64Request { Command = "scan", DeviceId = device.Device.ID, Name = device.Device.Name,
            Source = request.Source, Dpi = request.Dpi, Color = request.Color, WidthMm = request.Size?.WidthInMm,
            HeightMm = request.Size?.HeightInMm, NativeUi = request.NativeUi, Deskew = request.Deskew,
            Parent = request.Parent.ToInt64(), OutputFolder = outputFolder };
        await foreach (var response in Exchange(message, cancellationToken, null))
            if (response.Kind == "page" && response.Path != null) yield return response.Path;
    }
    private async IAsyncEnumerable<Twain64Response> Exchange(Twain64Request request,
        [EnumeratorCancellation] CancellationToken cancellationToken, TimeSpan? timeout)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("64-bit helper executable missing");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != null) deadline.CancelAfter(timeout.Value);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--twain64-worker");
        start.ArgumentList.Add("--parent-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(start) ?? throw new IOException("Could not start TWAIN helper");
        // Closing or cancelling the operation terminates only this operation's helper, including a hung driver.
        using var registration = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var errorDrain = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)); process.StandardInput.Close();
            while (true)
            {
                string? line;
                try { line = await process.StandardOutput.ReadLineAsync(deadline.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("TWAIN helper timeout"); }
                if (line == null) break;
                var response = JsonSerializer.Deserialize<Twain64Response>(line) ?? throw new IOException("Bad worker response");
                if (response.Kind == "error") throw new ScannerBackendException(response.ErrorType ?? "TwainError", response.ErrorCode);
                if (response.Kind == "done") yield break;
                yield return response;
            }
            await process.WaitForExitAsync(); await errorDrain;
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("TWAIN helper timeout");
            if (process.ExitCode != 0) throw new IOException("TWAIN helper failed");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        }
    }
}

public sealed class Twain64Request
{
    public string Command { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public PaperSource Source { get; set; }
    public BitDepth Color { get; set; }
    public int Dpi { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public bool NativeUi { get; set; }
    public bool Deskew { get; set; }
    public long Parent { get; set; }
    public string OutputFolder { get; set; } = "";
}
public sealed class Twain64Response
{
    public string Kind { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? ErrorType { get; set; }
    public int ErrorCode { get; set; }
    public Twain64Caps? Caps { get; set; }
}
public sealed class Twain64SourceCaps
{
    public PaperSource Source { get; set; }
    public int[]? Dpis { get; set; }
    public BitDepthCaps? Colors { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public PerSourceCaps ToCaps() => new() { DpiCaps = Dpis == null ? null : new DpiCaps { Values = System.Collections.Immutable.ImmutableList.CreateRange(Dpis) },
        BitDepthCaps = Colors, PageSizeCaps = WidthMm.HasValue && HeightMm.HasValue ? new PageSizeCaps { ScanArea = new(WidthMm.Value, HeightMm.Value, PageSizeUnit.Millimetre) } : null };
}
public sealed class Twain64Caps
{
    public PaperSourceCaps? Sources { get; set; }
    public List<Twain64SourceCaps> PerSource { get; set; } = new();
    public static Twain64Caps From(ScanCaps caps) => new() { Sources = caps.PaperSourceCaps,
        PerSource = new[] { PaperSource.Flatbed, PaperSource.Feeder, PaperSource.Duplex }.Select(source =>
        {
            var data = ScanCapabilities.ForSource(caps, source);
            return new Twain64SourceCaps { Source = source, Dpis = data?.DpiCaps?.Values?.ToArray(), Colors = data?.BitDepthCaps,
                WidthMm = data?.PageSizeCaps?.ScanArea?.WidthInMm, HeightMm = data?.PageSizeCaps?.ScanArea?.HeightInMm };
        }).ToList() };
    public ScanCaps ToCaps() => new() { PaperSourceCaps = Sources,
        FlatbedCaps = PerSource.FirstOrDefault(c => c.Source == PaperSource.Flatbed)?.ToCaps(),
        FeederCaps = PerSource.FirstOrDefault(c => c.Source == PaperSource.Feeder)?.ToCaps(),
        DuplexCaps = PerSource.FirstOrDefault(c => c.Source == PaperSource.Duplex)?.ToCaps() };
}
public static class Twain64Protocol
{
    public static async Task<int> Run()
    {
        static void Reply(Twain64Response response) { Console.WriteLine(JsonSerializer.Serialize(response)); Console.Out.Flush(); }
        try
        {
            var request = JsonSerializer.Deserialize<Twain64Request>(await Console.In.ReadLineAsync() ?? "") ?? throw new ArgumentException();
            using var messageLoop = new SdkTwainMessageLoop();
            using var context = new ScanningContext(new GdiImageContext());
            var controller = new ScanController(context);
            var options = new ScanOptions { Driver = Driver.Twain, TwainOptions = new TwainOptions { Dsm = TwainDsm.NewX64 } };
            if (request.Command == "discover")
            {
                await foreach (var device in controller.GetDevices(options)) Reply(new() { Kind = "device", DeviceId = device.ID, Name = device.Name });
            }
            else
            {
                options.Device = new ScanDevice(Driver.Twain, request.DeviceId, request.Name);
                if (request.Command == "caps") Reply(new() { Kind = "caps", Caps = Twain64Caps.From(await controller.GetCaps(options)) });
                else if (request.Command == "scan")
                {
                    options.PaperSource = request.Source; options.Dpi = request.Dpi; options.BitDepth = request.Color;
                    options.PageSize = request.WidthMm.HasValue && request.HeightMm.HasValue ? new PageSize(request.WidthMm.Value, request.HeightMm.Value, PageSizeUnit.Millimetre) : null;
                    options.UseNativeUI = request.NativeUi; options.AutoDeskew = request.Deskew; options.MaxQuality = true;
                    options.DialogParent = new IntPtr(request.Parent);
                    if (!options.UseNativeUI) ScanCapabilities.Validate(await controller.GetCaps(options), new(request.Source, request.Dpi, request.Color, options.PageSize, false, request.Deskew, options.DialogParent));
                    Directory.CreateDirectory(request.OutputFolder);
                    await foreach (var image in controller.Scan(options))
                    {
                        var path = System.IO.Path.Combine(request.OutputFolder, Guid.NewGuid().ToString("N") + ".png");
                        using (image) image.Save(path);
                        Reply(new() { Kind = "page", Path = path });
                    }
                }
                else throw new ArgumentException("Worker command");
            }
            Reply(new() { Kind = "done" });
            return 0;
        }
        catch (Exception e) { Reply(new() { Kind = "error", ErrorType = e.GetType().Name, ErrorCode = e.HResult }); return 1; }
    }
}

// NAPS2's native worker configures this pump internally. SDK 1.3 only publishes an x86 worker;
// this narrowly scoped adapter configures the same pinned SDK infrastructure for our x64 process.
// It is isolated from the application and bounded by the parent process's timeout/cancellation.
internal sealed class SdkTwainMessageLoop : IDisposable
{
    private object? _pump;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    public SdkTwainMessageLoop()
    {
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            try
            {
                var assembly = typeof(ScanController).Assembly;
                var pumpType = assembly.GetType("NAPS2.Platform.Windows.Win32MessagePump", true)!;
                _pump = pumpType.GetMethod("Create")!.Invoke(null, null)!;
                var managerType = assembly.GetType("NAPS2.Platform.Windows.Win32TwainHandleManager", true)!;
                var factoryProperty = assembly.GetType("NAPS2.Scan.Internal.Twain.TwainHandleManager", true)!.GetProperty("Factory")!;
                var createManager = System.Linq.Expressions.Expression.New(managerType.GetConstructor([pumpType])!, System.Linq.Expressions.Expression.Constant(_pump, pumpType));
                factoryProperty.SetValue(null, System.Linq.Expressions.Expression.Lambda(factoryProperty.PropertyType, createManager).Compile());
                assembly.GetType("NAPS2.Threading.Invoker", true)!.GetProperty("Current")!.SetValue(null, _pump);
                _ready.Set();
                pumpType.GetMethod("RunMessageLoop")!.Invoke(_pump, null);
            }
            catch (Exception e) { failure = e; _ready.Set(); }
        }) { IsBackground = true, Name = "TWAIN x64 message loop" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("TWAIN message loop initialization");
        if (failure != null) throw new InvalidOperationException("TWAIN SDK message loop initialization", failure);
    }
    public void Dispose()
    {
        if (_pump is IDisposable disposable) disposable.Dispose();
        if (_thread.Join(TimeSpan.FromSeconds(2))) _ready.Dispose();
    }
}
