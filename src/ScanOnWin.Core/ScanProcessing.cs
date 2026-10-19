using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScanOnWin.Core;

public static class ScanProcessing
{
    public static void Rotate(Bitmap bitmap, string output, int degrees)
    {
        bitmap.RotateFlip(degrees switch { 90 => RotateFlipType.Rotate90FlipNone, 180 => RotateFlipType.Rotate180FlipNone, 270 => RotateFlipType.Rotate270FlipNone, _ => throw new ArgumentOutOfRangeException(nameof(degrees)) });
        bitmap.Save(output, ImageFormat.Png);
    }
    public static void Crop(Bitmap bitmap, string output, Rectangle rectangle)
    {
        rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (rectangle.Width < 2 || rectangle.Height < 2) throw new ArgumentException("Empty crop");
        using var crop = bitmap.Clone(rectangle, PixelFormat.Format24bppRgb);
        crop.SetResolution(bitmap.HorizontalResolution, bitmap.VerticalResolution);
        crop.Save(output, ImageFormat.Png);
    }
    public static void Adjust(Bitmap bitmap, string output, double brightness, double contrast)
    {
        var factor = (float)Math.Clamp(1 + contrast / 100, 0, 2);
        var offset = (float)(brightness / 100 + (1 - factor) / 2);
        using var result = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        result.SetResolution(bitmap.HorizontalResolution, bitmap.VerticalResolution);
        using var graphics = Graphics.FromImage(result);
        using var attr = new ImageAttributes();
        attr.SetColorMatrix(new ColorMatrix(new[] {
            new[] { factor,0f,0f,0f,0f }, new[] { 0f,factor,0f,0f,0f }, new[] { 0f,0f,factor,0f,0f },
            new[] { 0f,0f,0f,1f,0f }, new[] { offset,offset,offset,0f,1f } }));
        graphics.DrawImage(bitmap, new Rectangle(0, 0, result.Width, result.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attr);
        result.Save(output, ImageFormat.Png);
    }
}
