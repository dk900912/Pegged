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
    private readonly LinearGradientBrush _flowBrush;
    private double _scale = 1.0;

    public EdgePillWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;

        // Flowing light: a gradient that slides back and forth.
        _flowBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        _flowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        _flowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0x4C, 0xC2, 0xFF), 0.35));
        _flowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0x7B, 0x61, 0xFF), 0.65));
        _flowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        Pill.Background = _flowBrush;
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

        // PlaceOn() ran before Show(): there was no HWND then, so the pill was
        // left at Windows' default spot (measured at 32,32). Do it now.
        MoveTo(_target.Left, _target.Top);
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
        _scale = monitor.Scale > 0 ? monitor.Scale : 1.0;
        MoveTo(left, top);
    }

    private void MoveTo(double dipLeft, double dipTop)
    {
        if (_handle == IntPtr.Zero) return;

        var scale = _scale > 0 ? _scale : GetDpiScale(_handle);
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

        // A slow breath, 2.4 seconds a cycle (spec 3.1): the line is idle, not
        // asleep. The pill lives on the screen edge, so it breathes between
        // 35% and 75% opacity rather than blinking.
        _phase += dt * (Math.PI * 2 / 2.4);
        if (_phase > Math.PI * 2) _phase -= Math.PI * 2;

        var breath = 0.5 + 0.5 * Math.Sin(_phase);   // 0..1
        var squash = 0.85 + 0.15 * breath;           // full..taller..full
        var glow = 0.35 + 0.40 * breath;             // 0.35..0.75

        PillScale.ScaleY = squash;
        Pill.Opacity = glow * _fade;
        Root.Opacity = _fade;

        // Flowing light: the accent gradient slides gently.
        var flow = (_phase / (Math.PI * 2)) * 0.4 - 0.2;
        _flowBrush.GradientStops[1].Offset = 0.25 + flow;
        _flowBrush.GradientStops[2].Offset = 0.55 + flow;
    }
}
