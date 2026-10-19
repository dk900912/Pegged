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
    // The slide is a spring, but not a loose one: the original comes down on a
    // 0.42s spring at 0.82 damping (a hair of bounce) and goes back up on a
    // 0.22s curve. Stiffness and damping follow from that: k = (2*pi/response)^2
    // and c = 2*zeta*sqrt(k). The first numbers here (46 / 2.6) were five times
    // less damped than the spec, so the dock kept wobbling for seconds.
    private readonly Spring _slide = new(0, 0, stiffness: 224, damping: 24.5);
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _revealed;
    private bool _pinned;
    private double _breezePhase;
    private double _scale = 1.0;

    /// <summary>Where the line hangs when it is down, in WPF units.</summary>
    private Rect _target = new(0, 0, 800, 210);

    /// <summary>The desktop behind the dock, kept so a theme switch can re-wash
    /// it without reading a screen the dock is now covering.</summary>
    private AcrylicMaterial? _material;

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
        Strip.PreviewRequested += OnPreviewRequested;
        Strip.CropRequested += OnCropRequested;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        SizeChanged += OnWindowSizeChanged;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewRequested(object? sender, LineItem item)
    {
        if (item.IsRecording || !System.IO.File.Exists(item.Path)) return;
        ShowPreview(item.Path);
    }

    private void OnCropRequested(object? sender, LineItem item)
    {
        if (item.IsRecording || !System.IO.File.Exists(item.Path)) return;
        ShowCrop(item.Path);
    }

    public void ShowPreview(string path)
    {
        var preview = new PreviewWindow(path);
        preview.CropRequested += (_, p) => ShowCrop(p);
        preview.Show();
    }

    public void ShowCrop(string path)
    {
        var crop = new CropWindow(path);
        if (crop.ShowDialog() == true && crop.CroppedPath is { } outPath)
        {
            // Add the new file to the line as its own capture.
            App.CaptureStore.Hang(outPath);
            if (Settings.Current.PeekAtNewCapture) App.Instance?.Line?.Peek(Settings.Current.RetractDelay * 1.5);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Strip.Relayout();
        LayoutRope();
    }

    /// <summary>
    /// With more on the line than fits, the wheel slides it along. Up goes
    /// towards the older photos (the line moves right under the pointer), the
    /// way a wheel moves any other list towards its beginning.
    ///
    /// Windows delivers the wheel to the window under the pointer even when it
    /// is not the active one, which is why a dock that never takes the focus
    /// still gets this at all.
    /// </summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Strip.CanScroll) return;

        Strip.Scroll(e.Delta > 0 ? LayoutMetrics.Spacing / 2 : -LayoutMetrics.Spacing / 2);
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
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        style &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style);

        // Module 4: the line window is also where the clipboard talks to.
        NativeMethods.AddClipboardFormatListener(_handle);

        // PlaceOn() before the window is shown cannot move anything: there is
        // no HWND yet and MoveTo() bails out. Now that the handle exists, do
        // the placement that was asked for. Without this the window sits at
        // Windows' default spot (measured at 96,96) instead of the top edge.
        if (_target.Width > 0) MoveTo(_target.Left, _target.Top - _slide.Value);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                // Whatever happens on the line, the app underneath keeps the focus.
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;

            case NativeMethods.WM_NCHITTEST:
                // Per-pixel click through: only the cards and the line's own
                // buttons are solid, everything else belongs to the window
                // underneath. Doing it here instead of with a global
                // WS_EX_TRANSPARENT keeps our own buttons clickable.
                handled = true;
                return (IntPtr)HitTest(lParam);

            case NativeMethods.WM_CLIPBOARDUPDATE:
                ClipboardWatcher.OnClipboardChanged();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>WM_NCHITTEST: HTCLIENT over something the user can actually
    /// touch, HTTRANSPARENT everywhere else.</summary>
    private int HitTest(IntPtr lParam)
    {
        if (!_revealed || _handle == IntPtr.Zero) return NativeMethods.HTTRANSPARENT;

        var packed = lParam.ToInt64();
        var screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));

        Point client;
        try
        {
            client = PointFromScreen(screen);
        }
        catch (InvalidOperationException)
        {
            // No presentation source yet: nothing is on screen to hit.
            return NativeMethods.HTTRANSPARENT;
        }

        if (client.X < 0 || client.Y < 0 || client.X > this.Width || client.Y > this.Height)
            return NativeMethods.HTTRANSPARENT;

        foreach (var rect in _hitRects)
        {
            var grown = rect;
            grown.Inflate(4, 4);
            if (grown.Contains(client)) return NativeMethods.HTCLIENT;
        }

        // The quick action bar: only its buttons are solid. The pill behind
        // them floats over the top of the cards, so letting the pill itself
        // take clicks made a dead strip across every photo underneath.
        if (Toolbar.Opacity > 0.4)
        {
            foreach (var button in ToolbarButtons)
            {
                if (button.IsVisible && RectOf(button).Contains(client))
                    return NativeMethods.HTCLIENT;
            }
        }

        return NativeMethods.HTTRANSPARENT;
    }

    private System.Windows.Controls.Button[] ToolbarButtons => new[] { ClearButton, PinButton, SettingsButton };

    private Rect RectOf(FrameworkElement element)
    {
        var origin = element.TranslatePoint(new Point(0, 0), this);
        var rect = new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
        rect.Inflate(4, 4);
        return rect;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FrameLoop.Subscribe(OnFrame);
        ApplyThemeBrushes();
        ThemeManager.ThemeChanged += (_, _) => ApplyThemeBrushes();

        Strip.Rebuild();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_handle != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_handle);
        FrameLoop.Unsubscribe(OnFrame);
        _source?.RemoveHook(WndProc);
        _material?.Dispose();
        _material = null;
    }

    private void ApplyThemeBrushes()
    {
        // Every brush in the tree is a DynamicResource and follows on its own.
        // The dock's glass is not a brush resource though - it is a picture of
        // the desktop - so it has to be washed with the new tint by hand, or
        // switching to dark left a light panel behind.
        LayoutRope();
        Paint();
    }

    // ---- geometry --------------------------------------------------------

    /// <summary>Places the strip on a monitor and remembers where it belongs.</summary>
    public void PlaceOn(MonitorDescription monitor)
    {
        Monitor = monitor;
        var scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        var left = monitor.Bounds.Left;
        var top = monitor.Bounds.Top;
        var width = monitor.Bounds.Width;
        var height = LayoutMetrics.PanelHeight;

        // Remember the monitor's own scale: MoveTo() multiplies by it, and the
        // window's "current" DPI is stale while it is still on another screen.
        _scale = scale;

        _target = new Rect(left, top, width, height);

        this.Width = width;
        this.Height = height + 24;   // room for the hover toolbar below the rope

        MoveTo(left, top - _slide.Value);
        LayoutRope();
        ApplyAcrylic();
    }

    /// <summary>
    /// Rebuilds the frosted material behind the dock. The capture is only ever
    /// taken while the line is tucked away above the top edge: at that moment
    /// the screen the dock is about to cover is not covered by us, so the
    /// snapshot is of the desktop and nothing else. The top 16px are left out
    /// of the capture because the edge pill lives there, and stretching the
    /// rest back over it is invisible under a blur this soft.
    ///
    /// While the dock is open there is nothing to capture - we are standing in
    /// front of it - so the last capture is reused and simply re-tinted.
    /// </summary>
    private void ApplyAcrylic()
    {
        if (_target.Width < 8) return;

        if (!_revealed)
        {
            var scale = _scale > 0 ? _scale : 1.0;
            var region = new Rect(_target.Left, _target.Top + 16,
                _target.Width, Math.Max(32, _target.Height - 16));

            if (AcrylicBackdrop.Capture(region, scale) is { } fresh)
            {
                _material?.Dispose();
                _material = fresh;
            }
        }

        Paint();
    }

    /// <summary>Paints the glass: the captured desktop, washed with the tint of
    /// whichever theme is in force right now.</summary>
    private void Paint()
    {
        var tint = TryFindResource("Palette.Acrylic") is Color c
            ? c
            : Color.FromRgb(0xF4, 0xF6, 0xFB);

        if (_material?.Finish(tint) is { } frosted)
        {
            Dock.Background = frosted;
            return;
        }

        // No screen to read: keep the plain translucent panel.
        if (TryFindResource("Brush.Acrylic") is Brush plain) Dock.Background = plain;
    }

    /// <summary>Moves the window without activating it, in physical pixels.
    /// Uses the scale of the monitor the window belongs to, not the one it is
    /// leaving, so a 150% screen is not shifted by the old screen's 125%.</summary>
    private void MoveTo(double dipLeft, double dipTop)
    {
        if (_handle == IntPtr.Zero) return;

        var scale = _scale > 0 ? _scale : GetDpiScale(_handle);

        NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(dipLeft * scale), (int)Math.Round(dipTop * scale),
            0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private double GetDpiScale(IntPtr hwnd)
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Draws the rope: a parabola from edge to edge, shadow, core and
    /// highlight, fading out at both ends.</summary>
    public void LayoutRope()
    {
        var width = Math.Max(1, this.Width);
        var sag = Math.Min(30, width * 0.018);
        var top = LayoutMetrics.RopeTop;

        // Breeze: a gentle sine wave sway when the line is empty.
        var cards = CardLayer.Children.Count;
        var breeze = cards == 0 ? Math.Sin(_breezePhase) * 2.5 : 0;

        var figure = new PathFigure { StartPoint = new Point(-20, top) };
        figure.Segments.Add(new QuadraticBezierSegment(
            new Point(width / 2, top + 2 * sag + breeze), new Point(width + 20, top), true));

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

        // Take the glass before anything moves: the window is still above the
        // screen, so what we capture is the desktop the dock is about to cover.
        ApplyAcrylic();

        _revealed = true;
        _slide.Stiffness = 224;      // 0.42s response
        _slide.Damping = 24.5;       // damping fraction 0.82
        _slide.Target = 0;
        UpdateHint();
    }

    /// <summary>Tucks the line back up above the top edge.</summary>
    public void Conceal()
    {
        _pinned = false;
        if (!_revealed) return;

        _revealed = false;
        _slide.Stiffness = 816;      // 0.22s, as the original's easeIn
        _slide.Damping = 57;         // critically damped: no bounce on the way out
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
        if (!_slide.AtRest)
        {
            _slide.Step(dt);
            MoveTo(_target.Left, _target.Top - _slide.Value);
        }

        // Breeze: runs even when the slide is at rest (it always is, most of
        // the time), or the rope would be frozen for the whole session.
        _breezePhase += dt * 0.628;
        if (CardLayer.Children.Count == 0) LayoutRope();
    }

    // ---- click through ---------------------------------------------------

    /// <summary>Replaces the card hit rectangles used for click through.</summary>
    public void SetHitRects(System.Collections.Generic.IEnumerable<Rect> rects)
    {
        _hitRects.Clear();
        _hitRects.AddRange(rects);
    }

    /// <summary>Called on every poll with the pointer in window units. The line
    /// is not allowed to steal the mouse, so it never sees a MouseEnter: the
    /// hover state of the dock and of the cards is driven from here instead.
    /// The actual click-through happens in <see cref="HitTest"/>.</summary>
    public void UpdatePointer(Point cursorInWindow)
    {
        if (!_revealed)
        {
            ShowToolbar(false);
            Strip.ClearHover();
            return;
        }

        var overDock = cursorInWindow.Y >= 0 && cursorInWindow.Y <= _target.Height;
        ShowToolbar(overDock || RectOf(Toolbar).Contains(cursorInWindow));

        var overCard = false;
        foreach (var rect in _hitRects)
        {
            var grown = rect;
            grown.Inflate(4, 4);
            if (grown.Contains(cursorInWindow)) { overCard = true; break; }
        }

        if (!overCard) Strip.ClearHover();
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
