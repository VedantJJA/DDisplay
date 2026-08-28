using System.Runtime.InteropServices;

namespace DDisplay.Core.Input;

/// <summary>
/// Receives normalized touch coordinates from the Android client and injects
/// them into Windows as mouse events on the virtual monitor's coordinate space.
///
/// v1: single-pointer mouse emulation via SendInput with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
/// v2 (future): multi-touch via InjectTouchInput (Windows 8+ pointer injection API).
/// </summary>
public sealed class InputInjector
{
    // Virtual desktop width and height -- needed to convert absolute coordinates.
    private static readonly int VirtualDesktopWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    private static readonly int VirtualDesktopHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
    private static readonly int VirtualDesktopLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
    private static readonly int VirtualDesktopTop = GetSystemMetrics(SM_YVIRTUALSCREEN);

    private System.Drawing.Rectangle _virtualMonitorRect;

    /// <summary>
    /// Sets the pixel rectangle of the virtual monitor in virtual-desktop coordinates.
    /// Call this once the virtual monitor is created and its position is known.
    /// </summary>
    public void SetVirtualMonitorRect(System.Drawing.Rectangle rect)
    {
        _virtualMonitorRect = rect;
    }

    /// <summary>
    /// Injects a touch/pointer event translated from normalized Android coordinates.
    /// </summary>
    /// <param name="eventType">"down", "move", "up", or "cancel"</param>
    /// <param name="normalizedX">Horizontal position, 0.0 = left edge, 1.0 = right edge.</param>
    /// <param name="normalizedY">Vertical position, 0.0 = top edge, 1.0 = bottom edge.</param>
    public void InjectPointerEvent(string eventType, double normalizedX, double normalizedY)
    {
        // Map normalized coords to virtual monitor pixel space.
        int monX = _virtualMonitorRect.Left + (int)(normalizedX * _virtualMonitorRect.Width);
        int monY = _virtualMonitorRect.Top + (int)(normalizedY * _virtualMonitorRect.Height);

        // Convert to SendInput absolute coordinates (0-65535 range across the virtual desktop).
        int absX = (int)((double)(monX - VirtualDesktopLeft) / VirtualDesktopWidth * 65535);
        int absY = (int)((double)(monY - VirtualDesktopTop) / VirtualDesktopHeight * 65535);
        absX = Math.Clamp(absX, 0, 65535);
        absY = Math.Clamp(absY, 0, 65535);

        uint mouseFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE;

        switch (eventType)
        {
            case "down":
                SendMouseInput(absX, absY, mouseFlags | MOUSEEVENTF_LEFTDOWN);
                break;
            case "move":
                SendMouseInput(absX, absY, mouseFlags);
                break;
            case "up":
            case "cancel":
                SendMouseInput(absX, absY, mouseFlags | MOUSEEVENTF_LEFTUP);
                break;
        }
    }

    private static void SendMouseInput(int absX, int absY, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = absX,
                dy = absY,
                mouseData = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };

        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    // ----- P/Invoke -----

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
