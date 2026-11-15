// FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-3000
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Fipinator.GaugeMaker;

/// <summary>
/// Takes a picture of the sim's window, for use as a reference picture only (never saved or shared).
/// Plain Windows calls (GDI), no extra components. The sim must be visible on screen when it runs.
/// </summary>
public static class ScreenCapture
{
    private static readonly string[] SimProcesses = ["FlightSimulator2024", "FlightSimulator"];

    /// <summary>The sim's window as a picture, or null with the reason in plain words.</summary>
    public static BitmapSource? CaptureSim(out string? problem)
    {
        problem = null;
        var window = SimProcesses.SelectMany(Process.GetProcessesByName)
            .Select(p => { try { return p.MainWindowHandle; } catch (InvalidOperationException) { return IntPtr.Zero; } })
            .FirstOrDefault(h => h != IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            problem = "The sim isn't running (or has no window yet). Start it, or use Open picture instead.";
            return null;
        }
        if (IsIconic(window))
        {
            problem = "The sim is minimized. Bring it up on screen and try again.";
            return null;
        }

        // Real screen pixels, whatever the display scaling.
        var oldContext = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
        try
        {
            if (!GetWindowRect(window, out var r) || r.Right - r.Left < 50 || r.Bottom - r.Top < 50)
            {
                problem = "Couldn't find where the sim's window is.";
                return null;
            }
            var (x, y, w, h) = (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, w, h);
            var old = SelectObject(memory, bitmap);
            try
            {
                if (!BitBlt(memory, 0, 0, w, h, screen, x, y, 0x00CC0020 /* SRCCOPY */ | 0x40000000 /* CAPTUREBLT */))
                {
                    problem = "Windows didn't allow the screenshot. Use Open picture with a screenshot you took instead.";
                    return null;
                }
                SelectObject(memory, old);
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
        finally
        {
            if (oldContext != IntPtr.Zero) SetThreadDpiAwarenessContext(oldContext);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
