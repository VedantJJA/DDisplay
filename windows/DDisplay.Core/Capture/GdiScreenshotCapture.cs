using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DDisplay.Core.Capture;

/// <summary>
/// Captures the Windows desktop as a JPEG screenshot using GDI CopyFromScreen.
/// Works reliably across all GPU architectures and multi-monitor configurations.
/// </summary>
public static class GdiScreenshotCapture
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public static byte[] CaptureDesktopJpeg(int quality = 75, bool captureAllScreens = true)
    {
        int x = captureAllScreens ? GetSystemMetrics(SM_XVIRTUALSCREEN) : 0;
        int y = captureAllScreens ? GetSystemMetrics(SM_YVIRTUALSCREEN) : 0;
        int width = captureAllScreens ? GetSystemMetrics(SM_CXVIRTUALSCREEN) : GetSystemMetrics(SM_CXSCREEN);
        int height = captureAllScreens ? GetSystemMetrics(SM_CYVIRTUALSCREEN) : GetSystemMetrics(SM_CYSCREEN);

        if (width <= 0 || height <= 0)
        {
            width = 1920;
            height = 1080;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        var encoder = GetEncoder(ImageFormat.Jpeg);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

        if (encoder != null)
        {
            bitmap.Save(ms, encoder, encoderParams);
        }
        else
        {
            bitmap.Save(ms, ImageFormat.Jpeg);
        }

        return ms.ToArray();
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(c => c.FormatID == format.Guid);
    }
}
