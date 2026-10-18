using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Pegged.Core;

namespace Pegged.Views;

/// <summary>
/// One capture on the line: a wooden peg and, hanging from it, the picture.
///
/// Nothing here is a WPF animation. The tilt is a real pendulum, the hover a
/// real spring, so a card nudged by the pointer keeps rocking after the
/// pointer has moved on.
/// </summary>
public partial class CardView : UserControl
{
    /// <summary>How far down the card the rope runs: the peg grips it here, and
    /// this is the point the card swings around.</summary>
    public const double PivotY = 14;

    private readonly Pendulum _swing = new();
    private readonly Spring _hover = new(0, 0, stiffness: 180, damping: 18);
    private readonly Spring _lift = new(0, 0, stiffness: 220, damping: 22);
    private readonly Spring _pegOpen = new(0, 0, stiffness: 260, damping: 24);
    private readonly Spring _pressScale = new(1, 1, stiffness: 300, damping: 28);

    private readonly Effect? _effectCard;
    private readonly Effect? _effectHover;

    private double _fallVelocity;
    private bool _falling;
    private double _copiedFor;
    private double _fade = 1;
    private bool _hoverLook;

    public LineItem Item { get; }

    /// <summary>Asked when the little cross is pressed.</summary>
    public event EventHandler? TakeDownRequested;

    /// <summary>Asked when the user double-clicks the card (open the preview).</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>Asked when the user wants the in-app quick crop.</summary>
    public event EventHandler? CropRequested;

    public CardView(LineItem item)
    {
        Item = item;
        InitializeComponent();

        DataContext = item;
        ThumbImage.Source = item.Thumb;
        NameText.Text = item.Name;
        VideoBadge.Visibility = item.IsRecording ? Visibility.Visible : Visibility.Collapsed;

        _swing.Rest = item.Tilt;
        _swing.Nudge(-13);   // it swings in, then settles

        item.PropertyChanged += OnItemChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        MouseEnter += (_, _) => item.Hovering = true;
        MouseLeave += (_, _) => { item.Hovering = false; item.Pressed = false; };

        // Single click: copy to clipboard. Double click: preview. Right
        // click: take down. Press-and-drag: OLE drag-out.
        PreviewMouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        PreviewMouseRightButtonDown += OnRightClick;

        _effectCard = TryFindResource("Effect.Card") as Effect;
        _effectHover = TryFindResource("Effect.CardHover") as Effect;

        ApplyMetrics();
        FrameLoop.Subscribe(StepFrame);
    }

    // ---- the peg and the picture ----------------------------------------

    private void ApplyMetrics()
    {
        var width = LayoutMetrics.CardWidth;
        var max = LayoutMetrics.PhotoMaxHeight;

        var aspect = Math.Clamp(Item.Aspect, 0.45, 3.2);
        var height = Math.Clamp(width / aspect, 56, max);

        this.Width = width;
        Photo.Width = width;
        Photo.Height = height;
        PhotoClip.Width = width;
        PhotoClip.Height = height;

        // Rounded corners: a Border does not clip its content by itself.
        var radius = LayoutMetrics.PhotoRadius;
        PhotoClip.Clip = new RectangleGeometry(new Rect(0, 0, width - 2, height - 2), radius, radius);

        VideoBadge.Visibility = Item.IsRecording ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.NewSize.Height.Equals(0))
        {
            // Whatever the card's size, it swings around the rope.
            Root.RenderTransformOrigin = new Point(0.5, PivotY / e.NewSize.Height);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LineItem.Thumb):
                ThumbImage.Source = Item.Thumb;
                Dispatcher.BeginInvoke(ApplyMetrics);
                break;

            case nameof(LineItem.Falling) when Item.Falling:
                _falling = true;
                _fallVelocity = 40;
                break;

            case nameof(LineItem.Copied) when Item.Copied:
                _copiedFor = 1.1;
                break;

            case nameof(LineItem.Flying) when Item.Flying:
                _lift.SnapTo(-190);
                break;
        }
    }

    // ---- the frame -------------------------------------------------------

    private void StepFrame(double dt)
    {
        // The pendulum: gravity pulls the card upright, the air slows it down.
        if (!_swing.AtRest)
        {
            _swing.Step(dt);
            Item.Swing = _swing.Angle - _swing.Rest;
        }

        Tilt.Angle = Item.Dragging ? _swing.Rest : _swing.Angle;

        // Hover: a small lift, the name and the cross coming in.
        _hover.Target = Item.Hovering ? 1 : 0;
        if (!_hover.AtRest) _hover.Step(dt);

        // Pressing pushes it in, dragging lifts it out of the line.
        var liftTarget = Item.Dragging ? 6 : Item.Pressed ? -1.5 : 0;
        _lift.Target = liftTarget;
        if (!_lift.AtRest) _lift.Step(dt);

        // Peg opens on hover: spring band drops, slats spread.
        _pegOpen.Target = Item.Hovering ? 1 : 0;
        if (!_pegOpen.AtRest) _pegOpen.Step(dt);
        var peg = _pegOpen.Value;
        PinBand.Margin = new Thickness(0, 11 + 2 * peg, 0, 0);
        PinSlatTop.Margin = new Thickness(0, -1 * peg, 0, 0);
        PinSlatBottom.Margin = new Thickness(0, 21 + 1 * peg, 0, 0);

        // Press scale: card squeezes to 0.95 when pressed.
        _pressScale.Target = Item.Pressed && !Item.Dragging ? 0.95 : 1.0;
        if (!_pressScale.AtRest) _pressScale.Step(dt);
        Press.ScaleX = _pressScale.Value;
        Press.ScaleY = _pressScale.Value;

        var pop = 1 + 0.035 * _hover.Value;
        Pop.ScaleX = pop;
        Pop.ScaleY = pop;

        NameBar.Opacity = Math.Clamp(_hover.Value * 1.2, 0, 1);
        CloseButton.Opacity = Math.Clamp(_hover.Value * 1.2, 0, 1);
        ActionRowBg.Opacity = Math.Clamp(_hover.Value * 1.2, 0, 1);

        // The shadow only swaps when it has to: re-assigning an effect every
        // frame would redo the layout work behind it.
        var hovered = _hover.Value > 0.5;
        if (hovered != _hoverLook)
        {
            _hoverLook = hovered;
            Photo.Effect = hovered ? _effectHover : _effectCard;
        }

        // "Copied!" pops and goes away on its own.
        if (_copiedFor > 0)
        {
            _copiedFor -= dt;
            var t = Math.Clamp(_copiedFor / 1.1, 0, 1);
            var grow = t > 0.75 ? (1 - t) / 0.25 : 1;      // pop in
            CopiedTag.Opacity = Math.Clamp(t * 3, 0, 1);
            CopiedScale.ScaleX = 0.6 + 0.4 * grow;
            CopiedScale.ScaleY = 0.6 + 0.4 * grow;

            if (_copiedFor <= 0) Item.Copied = false;
        }
        else
        {
            CopiedTag.Opacity = 0;
        }

        // Flying in from wherever it was captured.
        Slide.Y = _lift.Value;

        // Falling off the line: gravity, no spring.
        if (_falling)
        {
            _fallVelocity += 1500 * dt;
            Slide.Y += _fallVelocity * dt;
            _fade -= dt * 1.6;
            Root.Opacity = Math.Clamp(_fade, 0, 1);
        }

        Root.Opacity = Math.Clamp(_fade, 0, 1) * (Item.Dragging ? 0.7 : 1);
    }

    /// <summary>Gives the card a push, in degrees per second.</summary>
    public void Nudge(double degreesPerSecond)
    {
        _swing.Impulse(degreesPerSecond);
    }

    /// <summary>Settles the card at once: used after a drag.</summary>
    public void Settle()
    {
        _swing.SnapToRest();
        Item.Swing = 0;
    }

    // ---- plumbing --------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyMetrics();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FrameLoop.Unsubscribe(StepFrame);
        Item.PropertyChanged -= OnItemChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SizeChanged -= OnSizeChanged;
    }

    private void OnTakeDown(object sender, RoutedEventArgs e) => TakeDownRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        CopyToClipboard();
        e.Handled = true;
    }

    private void OnCrop(object sender, RoutedEventArgs e)
    {
        CropRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    // ---- input -----------------------------------------------------------

    private Point _pressAt;
    private bool _armed;
    private DateTime _lastClick;
    private const double DoubleClickMs = 400;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // The take-down button keeps its own click.
        if (e.OriginalSource is DependencyObject src && IsCloseButton(src)) return;

        _pressAt = e.GetPosition(this);
        _armed = true;
        Item.Pressed = true;
        CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!_armed) return;
        _armed = false;
        Item.Pressed = false;
        ReleaseMouseCapture();

        // Was it a click (no movement) or a drag (we passed the threshold)?
        var moved = (e.GetPosition(this) - _pressAt).Length;
        if (moved >= 4) { OnDragEnd(); return; }

        // Double-click detection: two quick clicks open the preview.
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastClick).TotalMilliseconds;
        _lastClick = now;
        if (elapsed > 0 && elapsed < DoubleClickMs)
        {
            PreviewRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        CopyToClipboard();
    }

    private void OnDragEnd()
    {
        Item.Dragging = true;
        try
        {
            var result = DragService.StartDrag(Window.GetWindow(this), new[] { Item.Path });
            if (result.Effects == DragService.DragKind.Move)
            {
                // The receiver took the file: we drop it from the line so
                // we are not keeping a phantom copy.
                TakeDownRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            Item.Dragging = false;
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        TakeDownRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static bool IsCloseButton(DependencyObject src)
    {
        while (src != null)
        {
            if (src is Button b && (b.Name == "CloseButton" || b.Name == "CopyButton" || b.Name == "CropButton" || b.Name == "DeleteButton")) return true;
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    private void CopyToClipboard()
    {
        try
        {
            if (Item.IsRecording)
            {
                Clipboard.SetText(Item.Path);
            }
            else if (Item.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     Item.Path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     Item.Path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     Item.Path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                var list = new System.Collections.Specialized.StringCollection { Item.Path };
                Clipboard.SetFileDropList(list);
            }
            else
            {
                Clipboard.SetText(Item.Path);
            }

            Item.Copied = true;
            if (Settings.Current.Sounds) SoundService.Play(SoundService.Sound.Copy);
        }
        catch
        {
            // Clipboard is busy: try again or just stay quiet.
        }
    }

    /// <summary>Where the picture itself is, relative to the card's top left.</summary>
    public Rect PhotoRect => new(0, 26, Photo.Width, Photo.Height);

    /// <summary>How tall the whole card is, peg included.</summary>
    public double CardHeight => Photo.Height + 32;
}
