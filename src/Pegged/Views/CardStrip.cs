using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pegged.Core;

namespace Pegged.Views;

/// <summary>
/// Lays the captures out along the rope and keeps them there: one card view
/// per capture, hung where the rope runs, oldest on the left.
///
/// It also tells the window where the cards are, so the window can let clicks
/// through everywhere except over them.
/// </summary>
public sealed class CardStrip
{
    private readonly Canvas _canvas;
    private readonly Dictionary<Guid, CardView> _views = new();
    private double _scroll;

    public CardStrip(Canvas canvas)
    {
        _canvas = canvas;
        App.CaptureStore.Changed += (_, _) => Dispatch();
        Settings.Saved += (_, _) => Dispatch();
    }

    /// <summary>Raised after every relayout: the window refreshes its hit rects.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when the user wants to open the preview for a card.</summary>
    public event EventHandler<LineItem>? PreviewRequested;

    /// <summary>Raised when the user wants the in-app quick crop for a card.</summary>
    public event EventHandler<LineItem>? CropRequested;

    /// <summary>The card views currently on the line, oldest first.</summary>
    public IEnumerable<CardView> Views => _views.Values.OrderBy(v => Canvas.GetLeft(v));

    private void Dispatch()
    {
        var action = Rebuild;
        if (_canvas.Dispatcher.CheckAccess()) action();
        else _canvas.Dispatcher.BeginInvoke(action);
    }

    /// <summary>Brings the views in step with what is on the line.</summary>
    public void Rebuild()
    {
        var live = App.CaptureStore.Live();

        var gone = _views.Keys.Where(id => live.All(i => i.Id != id)).ToList();
        foreach (var id in gone)
        {
            var v = _views[id];
            v.TakeDownRequested -= OnTakeDown;
            v.PreviewRequested -= OnPreview;
            v.CropRequested -= OnCrop;
            _canvas.Children.Remove(v);
            _views.Remove(id);
        }

        foreach (var item in live)
        {
            if (_views.ContainsKey(item.Id)) continue;

            var view = new CardView(item);
            view.TakeDownRequested += OnTakeDown;
            view.PreviewRequested += OnPreview;
            view.CropRequested += OnCrop;
            _views[item.Id] = view;
            _canvas.Children.Add(view);
        }

        Relayout();
    }

    /// <summary>Hangs every card where the rope runs.</summary>
    public void Relayout()
    {
        var width = _canvas.ActualWidth;
        if (width <= 0) width = _canvas.Width;
        if (width <= 0) return;

        App.CaptureStore.MaxItems = Settings.Current.KeepOnLine ?? LayoutMetrics.CapacityFor(width);

        var live = App.CaptureStore.Live();
        var visible = LayoutMetrics.CapacityFor(width);

        for (var index = 0; index < live.Count; index++)
        {
            if (!_views.TryGetValue(live[index].Id, out var view)) continue;

            var x = LayoutMetrics.XOf(index, live.Count, width, visible, _scroll);
            var left = x - view.Width / 2;
            var centre = x;
            var top = LayoutMetrics.RopeY(centre, width) - CardView.PivotY;

            Canvas.SetLeft(view, left);
            Canvas.SetTop(view, top);
            Canvas.SetZIndex(view, index);
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// True when the line is longer than the screen and the user asked for the
    /// spacing to be kept. Without "keep on line" the cards close ranks to fit
    /// instead, so there is nothing to scroll - and consuming the wheel then
    /// would be a dead gesture.
    /// </summary>
    public bool CanScroll =>
        Settings.Current.KeepOnLine is not null
        && App.CaptureStore.LiveCount > LayoutMetrics.CapacityFor(_canvas.ActualWidth);

    /// <summary>Slides the line sideways when there is more on it than fits.
    /// Zero is the newest end, positive moves towards the older ones, and the
    /// line stops at both ends: sliding past them only hides every card.</summary>
    public void Scroll(double amount)
    {
        var live = App.CaptureStore.Live();
        var visible = LayoutMetrics.CapacityFor(_canvas.ActualWidth);
        var span = Math.Max(0, live.Count - visible) * LayoutMetrics.Spacing;

        _scroll = Math.Clamp(_scroll + amount, 0, span);
        Relayout();
    }

    /// <summary>Where the pictures are, in window coordinates. Everything else
    /// on the line lets clicks through.
    ///
    /// The rect goes through the card's own transforms, because a hanging card
    /// is tilted and swaying: the picture on screen is not where the canvas
    /// left it, and a hit area that ignored the swing missed clicks along the
    /// edge of a card that had just been nudged.</summary>
    public IEnumerable<Rect> HitRects()
    {
        foreach (var view in _views.Values)
        {
            var left = Canvas.GetLeft(view);
            var top = Canvas.GetTop(view);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            var photo = view.PhotoWithTilt();
            yield return new Rect(left + photo.Left, top + photo.Top, photo.Width, photo.Height);
        }
    }

    /// <summary>Drops the hover look on every card. The line never sees a
    /// MouseLeave of its own (it is click-through), so the controller clears
    /// the hover state when the pointer is outside every card.</summary>
    public void ClearHover()
    {
        foreach (var view in _views.Values)
        {
            if (!view.Item.Hovering && !view.Item.Pressed) continue;
            view.Item.Hovering = false;
            view.Item.Pressed = false;
        }
    }

    /// <summary>Marks the card under the pointer as hovered and every other card
    /// as not.
    ///
    /// Hover must come from the pointer poll, not from WPF: the line is
    /// click-through, so the window stops receiving mouse messages the moment
    /// the pointer leaves a card. WPF therefore raises MouseEnter once and never
    /// the matching MouseLeave, and the card stays "entered" forever - the first
    /// hover would be the only one it ever gets, leaving the name strip and the
    /// copy/crop/delete row invisible for the rest of its life.
    /// </summary>
    public void UpdateHover(CardView? over)
    {
        foreach (var view in _views.Values)
        {
            var hovered = ReferenceEquals(view, over);
            if (view.Item.Hovering == hovered) continue;
            view.Item.Hovering = hovered;
            if (!hovered && !view.Item.Dragging) view.Item.Pressed = false;
        }
    }

    /// <summary>The card under a point in window coordinates, if any.</summary>
    public CardView? CardAt(Point point)
    {
        foreach (var view in _views.Values.Reverse())
        {
            var left = Canvas.GetLeft(view);
            var top = Canvas.GetTop(view);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            var photo = view.PhotoWithTilt();
            var r = new Rect(left + photo.Left, top + photo.Top, photo.Width, photo.Height);
            r.Inflate(6, 6);
            if (r.Contains(point)) return view;
        }

        return null;
    }

    private void OnTakeDown(object? sender, EventArgs e)
    {
        if (sender is CardView view) App.CaptureStore.Discard(view.Item.Id);
    }

    private void OnPreview(object? sender, EventArgs e)
    {
        if (sender is CardView view) PreviewRequested?.Invoke(this, view.Item);
    }

    private void OnCrop(object? sender, EventArgs e)
    {
        if (sender is CardView view) CropRequested?.Invoke(this, view.Item);
    }
}
