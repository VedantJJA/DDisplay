using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DDisplay.Core.Capture;

/// <summary>
/// Captures the extended virtual monitor (or specific display monitor) as a JPEG screenshot.
/// Automatically targets the secondary virtual display created by the Virtual Display Driver.
/// </summary>
public static class GdiScreenshotCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const int MONITORINFOF_PRIMARY = 1;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public record struct DisplayBounds(int Left, int Top, int Width, int Height, bool IsPrimary, string DeviceName);

    public static List<DisplayBounds> GetAllMonitors()
    {
        var monitors = new List<DisplayBounds>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                monitors.Add(new DisplayBounds(
                    mi.rcMonitor.Left,
                    mi.rcMonitor.Top,
                    mi.rcMonitor.Width,
                    mi.rcMonitor.Height,
                    isPrimary,
                    mi.szDevice
                ));
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public static DisplayBounds GetVirtualOrSecondaryDisplayBounds()
    {
        var monitors = GetAllMonitors();

        // 1. Look for secondary (non-primary) monitor
        var secondary = monitors.FirstOrDefault(m => !m.IsPrimary);
        if (secondary.Width > 0 && secondary.Height > 0)
        {
            return secondary;
        }

        // 2. If multiple monitors, pick second monitor
        if (monitors.Count > 1)
        {
            return monitors[1];
        }

        // 3. If only 1 monitor found, fallback to primary
        if (monitors.Count == 1)
        {
            return monitors[0];
        }

        return new DisplayBounds(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), true, "\\\\.\\DISPLAY1");
    }

    public static byte[] CaptureDesktopJpeg(int quality = 75, bool preferVirtualDisplay = true)
    {
        var bounds = preferVirtualDisplay
            ? GetVirtualOrSecondaryDisplayBounds()
            : (GetAllMonitors().FirstOrDefault(m => m.IsPrimary) is { Width: > 0 } primary ? primary : GetVirtualOrSecondaryDisplayBounds());

        int x = bounds.Left;
        int y = bounds.Top;
        int width = bounds.Width > 0 ? bounds.Width : 1920;
        int height = bounds.Height > 0 ? bounds.Height : 1080;

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
