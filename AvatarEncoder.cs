using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikTokScrapper.Core;

namespace TikTokScrapper;

public sealed class AvatarEncoder : IAvatarEncoder
{
    public void WriteJpeg(string input, string output)
    {
        try
        {
            using var stream = File.OpenRead(input);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
                throw new IOException("The profile avatar has unsupported dimensions.");
            // JPEG has no alpha channel. Composite transparent avatars against white.
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, frame.PixelWidth, frame.PixelHeight);
                drawing.DrawRectangle(Brushes.White, null, bounds);
                drawing.DrawImage(frame, bounds);
            }
            var bitmap = new RenderTargetBitmap(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var destination = File.Create(output);
            encoder.Save(destination);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            throw new IOException("The profile avatar could not be converted to JPEG on this PC.", ex);
        }
    }
}
