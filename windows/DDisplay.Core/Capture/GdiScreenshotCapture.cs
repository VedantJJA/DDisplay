using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DDisplay.Core.Capture;

/// <summary>
/// High-performance GDI-based desktop capture that accurately locates the virtual or secondary display,
/// renders the active hardware mouse cursor directly into the image, and encodes compressed JPEG frames.
/// </summary>
public static class GdiScreenshotCapture
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

    public record DisplayBounds(int Left, int Top, int Width, int Height, bool IsPrimary, string DeviceName);

    public static List<DisplayBounds> GetAllMonitors()
    {
        var monitors = new List<DisplayBounds>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                int w = Math.Abs(mi.rcMonitor.Right - mi.rcMonitor.Left);
                int h = Math.Abs(mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                bool isPrimary = (mi.dwFlags & 1) != 0;

                monitors.Add(new DisplayBounds(
                    mi.rcMonitor.Left,
                    mi.rcMonitor.Top,
                    w,
                    h,
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
        if (secondary != null && secondary.Width > 0 && secondary.Height > 0)
        {
            return secondary;
        }

        // 2. If multiple monitors, pick second monitor
        if (monitors.Count > 1)
        {
            return monitors[1];
        }

        // 3. Fallback to primary monitor
        if (monitors.Count == 1)
        {
            return monitors[0];
        }

        return new DisplayBounds(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), true, @"\\.\DISPLAY1");
    }

    public static byte[] CaptureDesktopJpeg(int quality = 70, bool preferVirtualDisplay = true)
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

            // Draw active hardware cursor cleanly into the frame
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (GetCursorInfo(out ci) && ci.flags == CURSOR_SHOWING && ci.hCursor != IntPtr.Zero)
            {
                int cursorX = ci.ptScreenPos.x - x;
                int cursorY = ci.ptScreenPos.y - y;

                if (cursorX >= -32 && cursorX <= width && cursorY >= -32 && cursorY <= height)
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        DrawIconEx(hdc, cursorX, cursorY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
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
