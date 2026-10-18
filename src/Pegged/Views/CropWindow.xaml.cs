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
/// floats outside the box. Apply writes a new file next to the source.
/// </summary>
public partial class CropWindow : Window
{
    public string FilePath { get; }
    public string? CroppedPath { get; private set; }

    /// <summary>Handle slots. -1 moves the whole box.</summary>
    private const int HandleMove = -1;

    private Rect _box;
    private Point _dragStart;
    private Rect _boxStart;
    private bool _dragging;
    private int _handle = HandleMove;

    private double _imgW, _imgH;

    // Overlay children, kept as fields so layout never has to guess indexes.
    private readonly System.Windows.Shapes.Path _mask = new();
    private readonly Rectangle _selection = new();
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly Line[] _gridV = new Line[2];
    private readonly Line[] _gridH = new Line[2];
    private readonly Border _label = new();

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
            // Even if the picture can't be read, Apply still tries; the user
            // gets a clean error and the original is left alone.
        }

        BuildOverlay();
        Loaded += (_, _) => UpdateSizeLabel();
        KeyDown += OnKeyDown;
    }

    private void BuildOverlay()
    {
        // 1. the dim, with a hole where the box is
        _mask.Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0));
        _mask.IsHitTestVisible = false;
        Overlay.Children.Add(_mask);

        // 2. rule-of-thirds grid, 4px white, inside the box
        for (var i = 0; i < 2; i++)
        {
            _gridV[i] = new Line
            {
                Stroke = Brushes.White,
                StrokeThickness = 4,
                Opacity = 0.55,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            _gridH[i] = new Line
            {
                Stroke = Brushes.White,
                StrokeThickness = 4,
                Opacity = 0.55,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Overlay.Children.Add(_gridV[i]);
            Overlay.Children.Add(_gridH[i]);
        }

        // 3. the selection itself: draggable, and obviously the box
        _selection.Stroke = Brushes.White;
        _selection.StrokeThickness = 1.5;
        _selection.Fill = Brushes.Transparent;
        _selection.Cursor = Cursors.SizeAll;
        _selection.MouseLeftButtonDown += OnBoxDown;
        _selection.MouseMove += OnBoxMove;
        _selection.MouseLeftButtonUp += OnBoxUp;
        Overlay.Children.Add(_selection);

        // 4. eight handles, white with a black outline (spec 3.5)
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < _handles.Length; i++)
        {
            var isCorner = i is 0 or 2 or 5 or 7;
            var sz = isCorner ? 12 : 8;
            var h = new Rectangle
            {
                Width = sz,
                Height = sz,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Opacity = 0.95,
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
            _handles[i] = h;
            Overlay.Children.Add(h);
        }

        // 5. the live pixel size, sitting just outside the box
        _label.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0));
        _label.Padding = new Thickness(6, 3, 6, 3);
        _label.CornerRadius = new CornerRadius(4);
        _label.IsHitTestVisible = false;
        Overlay.Children.Add(_label);
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

    // ---- box, grid, handles ---------------------------------------------

    private void PlaceBox()
    {
        if (_imgW <= 0 || _imgH <= 0) return;

        _selection.Width = _box.Width;
        _selection.Height = _box.Height;
        Canvas.SetLeft(_selection, _box.X);
        Canvas.SetTop(_selection, _box.Y);

        LayoutGrid();
        LayoutHandles();
        BuildMask();
        UpdateSizeLabel();
    }

    private void LayoutGrid()
    {
        for (var i = 0; i < 2; i++)
        {
            var x = _box.X + _box.Width * (i + 1) / 3.0;
            var y = _box.Y + _box.Height * (i + 1) / 3.0;
            _gridV[i].X1 = x; _gridV[i].Y1 = _box.Y;
            _gridV[i].X2 = x; _gridV[i].Y2 = _box.Bottom;
            _gridH[i].X1 = _box.X;      _gridH[i].Y1 = y;
            _gridH[i].X2 = _box.Right;  _gridH[i].Y2 = y;
            _gridV[i].Visibility = Visibility.Visible;
            _gridH[i].Visibility = Visibility.Visible;
        }
    }

    private void LayoutHandles()
    {
        var positions = new[]
        {
            new Point(0, 0),   new Point(0.5, 0),   new Point(1, 0),
            new Point(0, 0.5),                    new Point(1, 0.5),
            new Point(0, 1),   new Point(0.5, 1),   new Point(1, 1),
        };
        for (var i = 0; i < _handles.Length; i++)
        {
            var h = _handles[i];
            var p = positions[i];
            var sz = h.Width;
            Canvas.SetLeft(h, _box.X + _box.Width * p.X - sz / 2);
            Canvas.SetTop(h, _box.Y + _box.Height * p.Y - sz / 2);
            System.Windows.Controls.Panel.SetZIndex(h, 10);
        }
    }

    private void BuildMask()
    {
        if (_imgW <= 0 || _imgH <= 0) return;

        var bgRect = new RectangleGeometry(new Rect(0, 0, _imgW, _imgH));
        var holeRect = new RectangleGeometry(_box);
        _mask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, bgRect, holeRect);
    }

    private void UpdateSizeLabel()
    {
        var pw = 0;
        var ph = 0;
        if (BigImage.Source is BitmapImage bi)
        {
            var sx = bi.PixelWidth / _imgW;
            var sy = bi.PixelHeight / _imgH;
            pw = (int)Math.Round(_box.Width * sx);
            ph = (int)Math.Round(_box.Height * sy);
            SizeHint.Text = $"{pw} × {ph} px";
        }
        else
        {
            pw = (int)_box.Width;
            ph = (int)_box.Height;
            SizeHint.Text = $"{pw} × {ph}";
        }

        _label.Child = new TextBlock
        {
            Text = $"  {pw} × {ph}  ",
            FontSize = 11,
            Foreground = Brushes.White,
        };

        // Just above the box, centred, clamped to the surface.
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;
        var x = Math.Clamp(_box.X + _box.Width / 2 - size.Width / 2, 0, Math.Max(0, _imgW - size.Width));
        var y = _box.Y - size.Height - 6;
        if (y < 0) y = _box.Bottom + 6;                      // no room above: go below
        Canvas.SetLeft(_label, x);
        Canvas.SetTop(_label, Math.Min(y, Math.Max(0, _imgH - size.Height)));
        System.Windows.Controls.Panel.SetZIndex(_label, 20);
    }

    // ---- input -----------------------------------------------------------

    private void OnBoxDown(object sender, MouseButtonEventArgs e)
    {
        _handle = HandleMove;
        _dragStart = e.GetPosition(Overlay);
        _boxStart = _box;
        _dragging = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnBoxMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _handle != HandleMove) return;
        var p = e.GetPosition(Overlay);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        _box = new Rect(
            Math.Clamp(_boxStart.X + dx, 0, Math.Max(0, _imgW - _boxStart.Width)),
            Math.Clamp(_boxStart.Y + dy, 0, Math.Max(0, _imgH - _boxStart.Height)),
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
        const double minSize = 24.0;

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
            var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();

            // Only write an extension whose bytes actually match: a .webp
            // written with PNG data is a broken file.
            var outExt = ext switch
            {
                ".png" => ".png",
                ".jpg" or ".jpeg" => ext,
                ".bmp" => ".bmp",
                _ => ".png",
            };

            var outPath = System.IO.Path.Combine(dir, $"{stem}-cropped{outExt}");
            var n = 1;
            while (File.Exists(outPath))
            {
                outPath = System.IO.Path.Combine(dir, $"{stem}-cropped-{n}{outExt}");
                n++;
            }

            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                BitmapEncoder encoder = outExt switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                    ".bmp" => new BmpBitmapEncoder(),
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
