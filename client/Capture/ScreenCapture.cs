using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace RemoteDesk.Client.Capture;

/// <summary>Baseline CPU/GDI capture. Does not capture the secure desktop; WGC/GPU encoding is not implemented.</summary>
public sealed class ScreenCapture
{
    public int MonitorIndex { get; set; }
    public int MaxWidth { get; set; } = 1280;
    public long Quality { get; set; } = 70;
    public int MonitorCount => Screen.AllScreens.Length;
    public Rectangle Bounds
    {
        get
        {
            var monitors = Screen.AllScreens;
            if (monitors.Length == 0) throw new InvalidOperationException("Nenhum monitor disponível.");
            return monitors[Math.Clamp(MonitorIndex, 0, monitors.Length - 1)].Bounds;
        }
    }

    public byte[] CaptureJpeg()
    {
        var bounds = Bounds;
        using var original = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(original))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        var width = Math.Min(bounds.Width, Math.Clamp(MaxWidth, 320, 3840));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * (double)width / bounds.Width));
        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(original, new Rectangle(0, 0, width, height));
        }
        var encoder = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Clamp(Quality, 10, 95));
        using var output = new MemoryStream();
        scaled.Save(output, encoder, parameters);
        return output.ToArray();
    }
}
