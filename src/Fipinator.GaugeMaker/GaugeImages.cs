// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Fipinator.GaugeMaker;

/// <summary>Turns a rendered gauge (SkiaSharp bitmap) into something WPF can show.</summary>
public static class GaugeImages
{
    public static BitmapSource ToImage(SKBitmap bitmap)
    {
        using var bgra = bitmap.ColorType == SKColorType.Bgra8888 ? null : bitmap.Copy(SKColorType.Bgra8888);
        var source = bgra ?? bitmap;
        var image = new WriteableBitmap(source.Width, source.Height, 96, 96, PixelFormats.Pbgra32, null);
        image.WritePixels(new Int32Rect(0, 0, source.Width, source.Height), source.GetPixels(), source.ByteCount, source.RowBytes);
        image.Freeze();
        return image;
    }
}
