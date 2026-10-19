using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace Pegged.Core;

/// <summary>
/// Everything Pegged knows about the displays: their working areas in WPF
/// units, their per monitor DPI (PerMonitorV2) and which one the pointer is
/// on.
/// </summary>
public sealed class MonitorDescription
{
    public required Rect Work { get; init; }

    public required Rect Bounds { get; init; }

    public required double Scale { get; init; }

    public required IntPtr Handle { get; init; }

    public required string Name { get; init; }

    /// <summary>The strip along the top of this monitor that wakes the line.</summary>
    public Rect HotBand(double height)
        => new(Bounds.Left, Bounds.Top, Bounds.Width, Math.Max(height, 4));
}

public static class MonitorService
{
    /// <summary>How deep the wake-up strip at the top of a screen is, in WPF units.</summary>
    public const double HotBandHeight = 6;

    private static readonly List<MonitorDescription> Monitors = new();

    public static IReadOnlyList<MonitorDescription> All => Monitors;

    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref NativeMethods.RECT lprcMonitor, IntPtr dwData);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref NativeMethods.MONITORINFO lpmi);

    public static void Refresh()
    {
        Monitors.Clear();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT rect, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref info)) return true;

            var scale = 1.0;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;

            var bounds = new Rect(rect.Left / scale, rect.Top / scale,
                                  rect.Width / scale, rect.Height / scale);
            var work = new Rect(info.rcWork.Left / scale, info.rcWork.Top / scale,
                                info.rcWork.Width / scale, info.rcWork.Height / scale);

            Monitors.Add(new MonitorDescription
            {
                Handle = hMonitor,
                Name = hMonitor.ToString(),
                Bounds = bounds,
                Work = work,
                Scale = scale,
            });
            return true;
        }, IntPtr.Zero);

        if (Monitors.Count == 0)
        {
            // Never happens on a real desktop, but the line must still have
            // somewhere to hang.
            var fallback = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            Monitors.Add(new MonitorDescription
            {
                Handle = IntPtr.Zero,
                Name = "fallback",
                Bounds = fallback,
                Work = fallback,
                Scale = 1.0,
            });
        }
    }

    /// <summary>The monitor under the pointer, or the primary one.</summary>
    public static MonitorDescription MonitorAtCursor(out Point cursor)
    {
        RefreshIfEmpty();
        NativeMethods.GetCursorPos(out var p);
        cursor = new Point(p.X, p.Y);

        foreach (var m in Monitors)
            if (m.Bounds.Contains(cursor)) return m;

        return Monitors[0];
    }

    /// <summary>Whether the pointer is resting in the wake-up strip of a monitor.</summary>
    public static bool IsInHotBand(Point cursor, out MonitorDescription monitor)
    {
        RefreshIfEmpty();
        monitor = Monitors[0];

        foreach (var m in Monitors)
        {
            if (m.HotBand(HotBandHeight).Contains(cursor))
            {
                monitor = m;
                return true;
            }
        }

        return false;
    }

    private static void RefreshIfEmpty()
    {
        if (Monitors.Count == 0) Refresh();
    }

    /// <summary>True when a window is covering a whole monitor: the Windows
    /// shape of "a full screen app".</summary>
    public static bool IsFullScreen(MonitorDescription monitor)
    {
        var r = new NativeMethods.RECT
        {
            Left = (int)monitor.Bounds.Left,
            Top = (int)monitor.Bounds.Top,
            Right = (int)monitor.Bounds.Right,
            Bottom = (int)monitor.Bounds.Bottom,
        };

        var found = false;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (found) return false;
            if (hWnd == NativeMethods.GetShellWindow()) return true;
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            if (NativeMethods.IsFullScreenOn(hWnd, r))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
