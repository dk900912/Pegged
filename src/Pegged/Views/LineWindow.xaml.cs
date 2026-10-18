using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Pegged.Core;
using Pegged.Localization;

namespace Pegged.Views;

/// <summary>
/// The line: a transparent strip along the top of one screen that floats over
/// everything, never takes the focus and lets clicks through everywhere except
/// over the cards.
///
/// Three things Win32 has to be told, because WPF cannot say them:
///   WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE  - show without stealing the focus
///   WS_EX_TRANSPARENT                     - click through, toggled per card
///   WM_MOUSEACTIVATE -> MA_NOACTIVATE     - never activate, ever
/// </summary>
public partial class LineWindow : Window
{
    private readonly Spring _slide = new(0, 0, stiffness: 46, damping: 2.6);
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _revealed;
    private bool _pinned;
    private bool _transparent = true;

    /// <summary>Where the line hangs when it is down, in WPF units.</summary>
    private Rect _target = new(0, 0, 800, 210);

    /// <summary>Card hit rectangles in window coordinates, set by the layout.</summary>
    private readonly System.Collections.Generic.List<Rect> _hitRects = new();

    /// <summary>The cards hanging from the rope. Module 5 drives the dragging
    /// and the copying through it.</summary>
    public CardStrip Strip { get; }

    public LineWindow()
    {
        InitializeComponent();
        Strip = new CardStrip(CardLayer);
        Strip.LayoutChanged += (_, _) =>
        {
            SetHitRects(Strip.HitRects());
            UpdateHint();
        };

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        SizeChanged += OnWindowSizeChanged;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Strip.Relayout();
        LayoutRope();
    }

    /// <summary>With more on the line than fits, the wheel slides it along.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (App.CaptureStore.LiveCount <= LayoutMetrics.CapacityFor(this.Width)) return;

        Strip.Scroll(e.Delta < 0 ? -LayoutMetrics.Spacing / 2 : LayoutMetrics.Spacing / 2);
        e.Handled = true;
    }

    // ---- the Win32 side --------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        if (_handle == IntPtr.Zero) return;

        var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style);
        _transparent = true;

        // Module 4: the line window is also where the clipboard talks to.
        NativeMethods.AddClipboardFormatListener(_handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                // Whatever happens on the line, the app underneath keeps the focus.
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;

            case NativeMethods.WM_CLIPBOARDUPDATE:
                ClipboardWatcher.OnClipboardChanged();
                break;
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FrameLoop.Subscribe(OnFrame);
        MouseEnter += (_, _) => ShowToolbar(true);
        MouseLeave += (_, _) => ShowToolbar(false);
        ApplyThemeBrushes();
        ThemeManager.ThemeChanged += (_, _) => ApplyThemeBrushes();

        Strip.Rebuild();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_handle != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_handle);
        FrameLoop.Unsubscribe(OnFrame);
        _source?.RemoveHook(WndProc);
    }

    private void ApplyThemeBrushes()
    {
        // Nothing to rebuild: every brush is a DynamicResource. The rope and
        // hint are redrawn so gradients follow the theme at once.
        LayoutRope();
    }

    // ---- geometry --------------------------------------------------------

    /// <summary>Places the strip on a monitor and remembers where it belongs.</summary>
    public void PlaceOn(MonitorDescription monitor)
    {
        Monitor = monitor;
        var scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        var left = monitor.Work.Left;
        var top = monitor.Work.Top;
        var width = monitor.Work.Width;
        var height = LayoutMetrics.PanelHeight / 1.0;

        _target = new Rect(left, top, width, height);

        this.Width = width;
        this.Height = height + 24;   // room for the hover toolbar below the rope

        MoveTo(left, top - _slide.Value);
        LayoutRope();
    }

    /// <summary>Moves the window without activating it, in physical pixels.</summary>
    private void MoveTo(double dipLeft, double dipTop)
    {
        if (_handle == IntPtr.Zero) return;

        var dpi = GetDpiForWindow(_handle);
        var scale = dpi / 96.0;

        NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(dipLeft * scale), (int)Math.Round(dipTop * scale),
            0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Draws the rope: a parabola from edge to edge, shadow, core and
    /// highlight, fading out at both ends.</summary>
    public void LayoutRope()
    {
        var width = Math.Max(1, this.Width);
        var sag = Math.Min(30, width * 0.018);
        var top = LayoutMetrics.RopeTop;

        var figure = new PathFigure { StartPoint = new Point(-20, top) };
        figure.Segments.Add(new QuadraticBezierSegment(
            new Point(width / 2, top + 2 * sag), new Point(width + 20, top), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        RopeShadow.Data = geometry;
        RopeCore.Data = geometry;
        RopeHighlight.Data = geometry;

        RopeShadow.RenderTransform = new TranslateTransform(0, 1.2);
        RopeHighlight.RenderTransform = new TranslateTransform(0, -0.35);

        // The rope fades in from beyond both edges.
        var fade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.92));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));

        var mask = new RectangleGeometry(new Rect(-20, top - 10, width + 40, 60));
        RopeLayer.OpacityMask = fade;
        RopeLayer.Clip = mask;
    }

    // ---- showing and hiding ---------------------------------------------

    public bool Revealed => _revealed;

    /// <summary>Where the line is on screen right now, WPF units, including the
    /// slide offset. The controller uses it to know when the pointer left.</summary>
    public Rect CurrentRect =>
        new(_target.Left, _target.Top - _slide.Value, this.Width, this.Height);

    /// <summary>Which monitor the line is hanging on.</summary>
    public MonitorDescription? Monitor { get; private set; }

    /// <summary>Starts up already tucked away, without a first animation.</summary>
    public void SnapConcealed()
    {
        _revealed = false;
        _pinned = false;
        _slide.SnapTo(this.Height + 12);
        MoveTo(_target.Left, _target.Top - _slide.Value);
        UpdateHint();
        ShowToolbar(false);
    }

    /// <summary>Slides the line down, optionally keeping it there.</summary>
    public void Reveal(bool pinned)
    {
        _pinned = pinned;
        if (_revealed) return;

        _revealed = true;
        _slide.Target = 0;
        UpdateHint();
    }

    /// <summary>Tucks the line back up above the top edge.</summary>
    public void Conceal()
    {
        _pinned = false;
        if (!_revealed) return;

        _revealed = false;
        _slide.Target = this.Height + 12;
        UpdateHint();
    }

    public void TogglePin() => _pinned = !_pinned;

    public bool Pinned => _pinned;

    private void UpdateHint()
    {
        var show = _revealed && CardLayer.Children.Count == 0;
        Hint.Opacity = show ? 1 : 0;
    }

    internal void OnCardsChanged() => UpdateHint();

    private void ShowToolbar(bool show)
    {
        Toolbar.Opacity = show && _revealed ? 1 : 0;
    }

    // ---- the frame -------------------------------------------------------

    private void OnFrame(double dt)
    {
        if (_slide.AtRest) return;

        _slide.Step(dt);
        MoveTo(_target.Left, _target.Top - _slide.Value);
    }

    // ---- click through ---------------------------------------------------

    /// <summary>Replaces the card hit rectangles used for click through.</summary>
    public void SetHitRects(System.Collections.Generic.IEnumerable<Rect> rects)
    {
        _hitRects.Clear();
        _hitRects.AddRange(rects);
    }

    /// <summary>The window only accepts the mouse while the cursor is over a
    /// card: everywhere else clicks go to whatever is underneath.</summary>
    public void UpdateClickThrough(Point cursorInWindow)
    {
        var over = false;
        foreach (var r in _hitRects)
        {
            var inflated = r;
            inflated.Inflate(4, 4);
            if (inflated.Contains(cursorInWindow)) { over = true; break; }
        }

        SetClickThrough(!over);
    }

    private void SetClickThrough(bool through)
    {
        if (through == _transparent || _handle == IntPtr.Zero) return;

        var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
        if (through) style |= NativeMethods.WS_EX_TRANSPARENT;
        else style &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style);
        _transparent = through;
    }

    // ---- toolbar ---------------------------------------------------------

    private void OnClear(object sender, RoutedEventArgs e) => Cleared?.Invoke(this, EventArgs.Empty);

    private void OnPin(object sender, RoutedEventArgs e)
    {
        TogglePin();
        // A pin means "stay down"; an arrow means "go back up when I leave".
        PinButton.Content = _pinned ? "\U0001F4CC" : "\u2B06";
        PinButton.ToolTip = Loc.Get(_pinned ? "Tray.ShowLine" : "Tray.HideLine");
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? Cleared;

    public event EventHandler? SettingsRequested;

    public Canvas CardCanvas => CardLayer;
}
