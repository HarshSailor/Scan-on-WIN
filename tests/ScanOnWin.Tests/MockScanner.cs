using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NAPS2.Scan;
using NAPS2.Images;
using ScanOnWin.Core;

namespace ScanOnWin.Tests;

// Test-only backend. Never registered in the shipped application or discovery list.
internal sealed class MockScanner(int pageCount = 1, Exception? failure = null) : IScannerBackend
{
    public string Id => "test-mock";
    public string Label => "TEST ONLY — MockScanner";
    public ScannerDevice Device => new(new ScanDevice(Driver.Wia, "mock", "Test scanner"), this);
    public Task<IReadOnlyList<ScannerDevice>> Discover(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ScannerDevice>>([Device]);
    public Task<ScanCaps> Capabilities(ScannerDevice device, CancellationToken cancellationToken) => Task.FromResult(Caps);
    public static ScanCaps Caps => new()
    {
        PaperSourceCaps = new PaperSourceCaps { SupportsFlatbed = true, SupportsFeeder = true, SupportsDuplex = true },
        FlatbedCaps = PerSource, FeederCaps = PerSource, DuplexCaps = PerSource
    };
    private static PerSourceCaps PerSource => new()
    {
        DpiCaps = new DpiCaps { Values = ImmutableList.Create(75, 150, 200, 300, 600) },
        BitDepthCaps = new BitDepthCaps { SupportsColor = true, SupportsGrayscale = true, SupportsBlackAndWhite = true },
        PageSizeCaps = new PageSizeCaps { ScanArea = PageSize.A4 }
    };
    public async IAsyncEnumerable<string> Scan(ScannerDevice device, ScanRequest request, string outputFolder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ScanCapabilities.Validate(Caps, request);
        Directory.CreateDirectory(outputFolder);
        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure != null) throw failure;
            await Task.Delay(15, cancellationToken);
            var path = Path.Combine(outputFolder, $"mock-{Guid.NewGuid():N}.png");
            CreateFixture(path, i + 1);
            yield return path;
        }
    }
    public static void CreateFixture(string path, int page = 1)
    {
        using var image = new Bitmap(1654, 2339); image.SetResolution(200, 200);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.White); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Arial", 40, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString("SCAN ON WIN TEST DOCUMENT", font, Brushes.Black, 120, 180);
            g.DrawString("Hello world. Document scanner.", font, Brushes.Black, 120, 290);
            g.DrawString("Привет мир. Проверка документа.", font, Brushes.Black, 120, 400);
            g.DrawString($"Page {page}. 1234567890", font, Brushes.Black, 120, 510);
            g.DrawRectangle(Pens.Teal, 120, 660, 1250, 80);
        }
        image.Save(path, ImageFormat.Png);
    }
}
