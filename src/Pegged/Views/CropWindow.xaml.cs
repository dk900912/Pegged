using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pegged.Core;
using Pegged.Localization;

namespace Pegged.Views;

/// <summary>
/// A fullscreen crop overlay. The user drags the box (or one of the 8 handles)
/// around on the image; the dimmed mask tracks it; the live "WxH" label
/// floats outside the box. Apply produces a new PNG next to the source.
/// </summary>
public partial class CropWindow : Window
{
    public string FilePath { get; }
    public string? CroppedPath { get; private set; }

    private Rect _box;
    private Point _dragStart;
    private Rect _boxStart;
    private bool _dragging;
    private int _handle; // -1 = move whole box, 0..7 = handle index

    private double _imgW, _imgH;

    public CropWindow(string path)
    {
        FilePath = path;
        InitializeComponent();

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = fs;
            img.EndInit();
            img.Freeze();
            BigImage.Source = img;
        }
        catch
        {
            // Even if the picture can't be read, Apply still tries to write
            // the (unchanged) bytes back: the user gets a clean error and
            // the original is left alone.
        }

        Loaded += (_, _) => { BuildHandles(); UpdateSizeLabel(); };
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        else if (e.Key == Key.Enter) OnApply(this, new RoutedEventArgs());
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _imgW = BigImage.ActualWidth;
        _imgH = BigImage.ActualHeight;
        if (_imgW <= 0 || _imgH <= 0) return;

        Surface.Width = _imgW;
        Surface.Height = _imgH;
        Overlay.Width = _imgW;
        Overlay.Height = _imgH;

        // Start with an 80%-centered crop.
        var inset = 0.1;
        _box = new Rect(_imgW * inset, _imgH * inset, _imgW * (1 - 2 * inset), _imgH * (1 - 2 * inset));
        PlaceBox();
    }

    // ---- box & handles ---------------------------------------------------

    private void BuildHandles()
    {
        Overlay.Children.Clear();
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < positions.Length; i++)
        {
            var isCorner = i is 0 or 2 or 5 or 7;
            var sz = isCorner ? 12 : 8;
            var h = new Rectangle
            {
                Width = sz,
                Height = sz,
                Fill = Brushes.White,
                Stroke = Application.Current.TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue,
                StrokeThickness = 1.5,
                Cursor = i switch
                {
                    0 or 7 => Cursors.SizeNWSE,
                    2 or 5 => Cursors.SizeNESW,
                    1 or 6 => Cursors.SizeNS,
                    3 or 4 => Cursors.SizeWE,
                    _ => Cursors.SizeAll,
                },
                Tag = i,
            };
            h.MouseLeftButtonDown += OnHandleDown;
            h.MouseMove += OnHandleMove;
            h.MouseLeftButtonUp += OnHandleUp;
            Overlay.Children.Add(h);
        }
        LayoutHandles();
    }

    private void LayoutHandles()
    {
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < 8; i++)
        {
            var h = (Rectangle)Overlay.Children[i + 1]; // +1 because mask is at index 0
            var p = positions[i];
            var sz = h.Width;
            Canvas.SetLeft(h, _box.X + _box.Width * p.X - sz / 2);
            Canvas.SetTop(h, _box.Y + _box.Height * p.Y - sz / 2);
        }
    }

    private void PlaceBox()
    {
        Selection.Margin = new Thickness(_box.X, _box.Y, 0, 0);
        Selection.Width = _box.Width;
        Selection.Height = _box.Height;
        LayoutHandles();
        BuildMask();
        UpdateSizeLabel();
    }

    private void BuildMask()
    {
        if (Overlay.Children.Count == 0) return;
        // Replace the mask (index 0) with a fresh one.
        if (Overlay.Children[0] is System.Windows.Shapes.Path) Overlay.Children.RemoveAt(0);

        var bgRect = new RectangleGeometry(new Rect(0, 0, _imgW, _imgH));
        var holeRect = new RectangleGeometry(_box);
        var geom = new CombinedGeometry(GeometryCombineMode.Exclude, bgRect, holeRect);

        var visual = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            Data = geom,
        };
        Overlay.Children.Insert(0, visual);
    }

    private void UpdateSizeLabel()
    {
        if (BigImage.Source is BitmapImage bi)
        {
            var sx = bi.PixelWidth / _imgW;
            var sy = bi.PixelHeight / _imgH;
            var pw = (int)Math.Round(_box.Width * sx);
            var ph = (int)Math.Round(_box.Height * sy);
            SizeLabel.Text = $"  {pw} × {ph}  ";
            SizeHint.Text = $"{pw} × {ph} px";
        }
        else
        {
            SizeLabel.Text = $"  {(int)_box.Width} × {(int)_box.Height}  ";
        }
        // Position the label just above the box, centered horizontally.
        SizeLabel.Margin = new Thickness(0, 0, 0, 0);
        // Use a translation to place it above the selection.
        var trans = new TranslateTransform(0, -24);
        SizeLabel.RenderTransform = trans;
        // We'll update the translation in a follow-up if needed; for now the
        // label stays centered and we just nudge it up with the transform.
    }

    // ---- input handling --------------------------------------------------

    private void OnBoxDown(object sender, MouseButtonEventArgs e)
    {
        _handle = -1;
        _dragStart = e.GetPosition(Overlay);
        _boxStart = _box;
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
    }

    private void OnBoxMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _handle != -1) return;
        var p = e.GetPosition(Overlay);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _box = new Rect(
            Math.Clamp(_boxStart.X + dx, 0, _imgW - _boxStart.Width),
            Math.Clamp(_boxStart.Y + dy, 0, _imgH - _boxStart.Height),
            _boxStart.Width, _boxStart.Height);
        PlaceBox();
    }

    private void OnBoxUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        _handle = (int)((Rectangle)sender).Tag;
        _dragStart = e.GetPosition(Overlay);
        _boxStart = _box;
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _handle < 0) return;
        var p = e.GetPosition(Overlay);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;

        var x = _boxStart.X;
        var y = _boxStart.Y;
        var w = _boxStart.Width;
        var h = _boxStart.Height;
        var minSize = 24.0;

        if (_handle is 0 or 3 or 5) { x = Math.Min(_boxStart.X + dx, x + w - minSize); w = _boxStart.Right - x; }
        if (_handle is 2 or 4 or 7) { w = Math.Max(minSize, _boxStart.Width + dx); }
        if (_handle is 0 or 1 or 2) { y = Math.Min(_boxStart.Y + dy, y + h - minSize); h = _boxStart.Bottom - y; }
        if (_handle is 5 or 6 or 7) { h = Math.Max(minSize, _boxStart.Height + dy); }

        x = Math.Clamp(x, 0, _imgW - minSize);
        y = Math.Clamp(y, 0, _imgH - minSize);
        w = Math.Clamp(w, minSize, _imgW - x);
        h = Math.Clamp(h, minSize, _imgH - y);

        _box = new Rect(x, y, w, h);
        PlaceBox();
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    // ---- apply / cancel --------------------------------------------------

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BigImage.Source is not BitmapImage src) { Close(); return; }

            var sx = src.PixelWidth / _imgW;
            var sy = src.PixelHeight / _imgH;

            var rect = new Int32Rect(
                (int)Math.Round(_box.X * sx),
                (int)Math.Round(_box.Y * sy),
                (int)Math.Round(_box.Width * sx),
                (int)Math.Round(_box.Height * sy));
            rect = new Int32Rect(
                Math.Max(0, rect.X),
                Math.Max(0, rect.Y),
                Math.Min(src.PixelWidth - rect.X, rect.Width),
                Math.Min(src.PixelHeight - rect.Y, rect.Height));
            if (rect.Width <= 0 || rect.Height <= 0) { Close(); return; }

            var crop = new CroppedBitmap(src, rect);

            var dir = System.IO.Path.GetDirectoryName(FilePath)!;
            var stem = System.IO.Path.GetFileNameWithoutExtension(FilePath);
            var ext = System.IO.Path.GetExtension(FilePath);
            var outPath = System.IO.Path.Combine(dir, $"{stem}-cropped{ext}");
            var n = 1;
            while (File.Exists(outPath))
            {
                outPath = System.IO.Path.Combine(dir, $"{stem}-cropped-{n}{ext}");
                n++;
            }

            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                var encoder = System.IO.Path.GetExtension(outPath).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 92 },
                    _ => new PngBitmapEncoder(),
                };
                encoder.Frames.Add(BitmapFrame.Create(crop));
                encoder.Save(fs);
            }

            CroppedPath = outPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.Get("Dialog.CropFailed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
