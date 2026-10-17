using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Pegged.Core;

namespace Pegged.Views;

/// <summary>
/// The line at rest: four pixels of light along the top edge of the screen,
/// breathing slowly so you remember it is there.
///
/// Same three Win32 promises as <see cref="LineWindow" />: never appear in the
/// taskbar, never take the focus, never eat a click.
/// </summary>
public partial class EdgePillWindow : Window
{
    private IntPtr _handle;
    private HwndSource? _source;
    private double _phase;
    private double _fade = 1;
    private bool _visible = true;
    private Rect _target = new(0, 0, 160, 12);

    public EdgePillWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        if (_handle == IntPtr.Zero) return;

        // TOOLWINDOW keeps it out of the taskbar and out of Alt+Tab,
        // NOACTIVATE keeps the focus where it is, TRANSPARENT lets every click
        // through to whatever is under the top edge.
        var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)NativeMethods.MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => FrameLoop.Subscribe(OnFrame);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        => FrameLoop.Unsubscribe(OnFrame);

    // ---- placement ------------------------------------------------------

    /// <summary>Centres the pill along the top edge of a monitor.</summary>
    public void PlaceOn(MonitorDescription monitor)
    {
        var left = monitor.Bounds.Left + (monitor.Bounds.Width - this.Width) / 2;
        var top = monitor.Bounds.Top - 1;
        _target = new Rect(left, top, this.Width, this.Height);
        MoveTo(left, top);
    }

    private void MoveTo(double dipLeft, double dipTop)
    {
        if (_handle == IntPtr.Zero) return;

        var scale = GetDpiScale(_handle);
        NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(dipLeft * scale), (int)Math.Round(dipTop * scale),
            0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static double GetDpiScale(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            if (dpi > 0) return dpi / 96.0;
        }
        catch (Exception)
        {
            // Windows 10 1607 and earlier do not have it: 1:1 is fine.
        }

        return 1.0;
    }

    // ---- breathing ------------------------------------------------------

    /// <summary>Shows the pill, or fades it away under the full line.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
    }

    private void OnFrame(double dt)
    {
        // Fade first: the pill dissolves as the line comes down.
        var wanted = _visible ? 1.0 : 0.0;
        var rate = dt / 0.18;
        _fade += Math.Clamp(wanted - _fade, -rate, rate);

        if (_fade <= 0.001)
        {
            if (this.Visibility != Visibility.Hidden) this.Visibility = Visibility.Hidden;
            return;
        }

        if (this.Visibility != Visibility.Visible) this.Visibility = Visibility.Visible;

        // A slow breath, four seconds a cycle: the line is idle, not asleep.
        _phase += dt * (Math.PI * 2 / 4.0);
        if (_phase > Math.PI * 2) _phase -= Math.PI * 2;

        var breath = 0.5 + 0.5 * Math.Sin(_phase);   // 0..1
        var squash = 0.45 + 0.55 * breath;           // thin..full
        var glow = 0.55 + 0.35 * breath;

        PillScale.ScaleY = squash;
        Pill.Opacity = glow * _fade;
        Root.Opacity = _fade;
    }
}
