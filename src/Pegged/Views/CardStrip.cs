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

    /// <summary>Slides the line sideways when there is more on it than fits.</summary>
    public void Scroll(double amount)
    {
        var live = App.CaptureStore.Live();
        var visible = LayoutMetrics.CapacityFor(_canvas.ActualWidth);
        var span = Math.Max(0, live.Count - visible) * LayoutMetrics.Spacing;

        _scroll = Math.Clamp(_scroll + amount, -span, span);
        Relayout();
    }

    /// <summary>Where the pictures are, in window coordinates. Everything else
    /// on the line lets clicks through.</summary>
    public IEnumerable<Rect> HitRects()
    {
        foreach (var view in _views.Values)
        {
            var left = Canvas.GetLeft(view);
            var top = Canvas.GetTop(view);
            if (double.IsNaN(left) || double.IsNaN(top)) continue;

            var photo = view.PhotoRect;
            yield return new Rect(left + photo.Left, top + photo.Top, photo.Width, photo.Height);
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

            var photo = view.PhotoRect;
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
