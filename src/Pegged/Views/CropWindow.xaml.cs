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
/// A modal crop overlay. The user drags the box (or one of the 8 handles)
/// around on the image; the dimmed mask tracks it; the live "WxH" label
/// follows the box. Apply produces a new PNG next to the source.
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

    public CropWindow(string path)
    {
        FilePath = path;
        InitializeComponent();

        TitleText.Text = System.IO.Path.GetFileName(path);

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

            // Surface size tracks the image (max 800x500 with aspect).
            var aspect = img.PixelWidth / (double)Math.Max(1, img.PixelHeight);
            double w = 800, h = 800 / aspect;
            if (h > 500) { h = 500; w = 500 * aspect; }
            Surface.Width = w;
            Surface.Height = h;
            BoxLayer.Width = w;
            BoxLayer.Height = h;
            Selection.Width = w;
            Selection.Height = h;

            // Start with an 80%-centered crop.
            var inset = 0.1;
            _box = new Rect(w * inset, h * inset, w * (1 - 2 * inset), h * (1 - 2 * inset));
            PlaceBox();
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

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e) { }

    // ---- box & handles ---------------------------------------------------

    private void BuildHandles()
    {
        BoxLayer.Children.Clear();
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < positions.Length; i++)
        {
            var h = new Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.White,
                Stroke = Application.Current.TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue,
                StrokeThickness = 1.5,
                Cursor = i switch
                {
                    0 or 4 => Cursors.SizeNWSE,
                    1 or 5 => Cursors.SizeNS,
                    2 or 6 => Cursors.SizeNESW,
                    3 or 7 => Cursors.SizeWE,
                    _ => Cursors.SizeAll,
                },
                Tag = i,
            };
            h.MouseLeftButtonDown += OnHandleDown;
            h.MouseMove += OnHandleMove;
            h.MouseLeftButtonUp += OnHandleUp;
            BoxLayer.Children.Add(h);
        }
        LayoutHandles();
    }

    private void LayoutHandles()
    {
        if (BoxLayer.Children.Count < 8) return;
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < 8; i++)
        {
            var h = (Rectangle)BoxLayer.Children[i];
            var p = positions[i];
            Canvas.SetLeft(h, _box.X + _box.Width * p.X - 6);
            Canvas.SetTop(h, _box.Y + _box.Height * p.Y - 6);
        }
    }

    private void PlaceBox()
    {
        Canvas.SetLeft(Selection, _box.X);
        Canvas.SetTop(Selection, _box.Y);
        Selection.Width = _box.Width;
        Selection.Height = _box.Height;
        LayoutHandles();
        BuildMask();
        UpdateSizeLabel();
    }

    private void BuildMask()
    {
        MaskLayer.Children.Clear();
        var w = BoxLayer.ActualWidth;
        var h = BoxLayer.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Use a CombinedGeometry so the dim is everywhere except the box.
        var bgRect = new RectangleGeometry(new Rect(0, 0, w, h));
        var holeRect = new RectangleGeometry(_box);
        var geom = new CombinedGeometry(GeometryCombineMode.Exclude, bgRect, holeRect);

        var visual = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            Data = geom,
        };
        MaskLayer.Children.Add(visual);
    }

    private void UpdateSizeLabel()
    {
        var w = BoxLayer.ActualWidth;
        var h = BoxLayer.ActualHeight;
        if (BigImage.Source is BitmapImage bi)
        {
            var sx = bi.PixelWidth / w;
            var sy = bi.PixelHeight / h;
            var pw = (int)Math.Round(_box.Width * sx);
            var ph = (int)Math.Round(_box.Height * sy);
            SizeLabel.Text = $"  {pw} × {ph}  ";
            SizeHint.Text = $"{pw} × {ph} px";
        }
        else
        {
            SizeLabel.Text = $"  {(int)_box.Width} × {(int)_box.Height}  ";
        }
        Canvas.SetLeft(SizeLabel, _box.X + 4);
        Canvas.SetTop(SizeLabel, _box.Y + 4);
    }

    // ---- input handling --------------------------------------------------

    private void OnBoxDown(object sender, MouseButtonEventArgs e)
    {
        _handle = -1;
        _dragStart = e.GetPosition(BoxLayer);
        _boxStart = _box;
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
    }

    private void OnBoxMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _handle != -1) return;
        var p = e.GetPosition(BoxLayer);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _box = new Rect(
            Math.Clamp(_boxStart.X + dx, 0, BoxLayer.ActualWidth - _boxStart.Width),
            Math.Clamp(_boxStart.Y + dy, 0, BoxLayer.ActualHeight - _boxStart.Height),
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
        _dragStart = e.GetPosition(BoxLayer);
        _boxStart = _box;
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _handle < 0) return;
        var p = e.GetPosition(BoxLayer);
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

        x = Math.Clamp(x, 0, BoxLayer.ActualWidth - minSize);
        y = Math.Clamp(y, 0, BoxLayer.ActualHeight - minSize);
        w = Math.Clamp(w, minSize, BoxLayer.ActualWidth - x);
        h = Math.Clamp(h, minSize, BoxLayer.ActualHeight - y);

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

            var w = BoxLayer.ActualWidth;
            var h = BoxLayer.ActualHeight;
            var sx = src.PixelWidth / w;
            var sy = src.PixelHeight / h;

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

            // Save next to the original with a -cropped-N suffix.
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
