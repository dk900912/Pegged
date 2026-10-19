using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DBitmap = System.Drawing.Bitmap;
using DColor = System.Drawing.Color;
using DGraphics = System.Drawing.Graphics;
using DImaging = System.Drawing.Imaging;

namespace Pegged.Core;

/// <summary>
/// The acrylic behind the dock.
///
/// Windows will not give a layered window (one with per-pixel transparency,
/// which the line needs for its rounded corners) a DWM backdrop: acrylic and
/// Mica are composited underneath the window and a layered window paints over
/// the whole thing. So the material is built the way it is defined instead of
/// asked for: capture what is behind the dock, blur it, tint it, sprinkle the
/// noise, and paint that as the dock's own background.
///
/// The capture is taken while the line is still tucked above the top edge, so
/// the snapshot never contains the line itself. It is a snapshot, not a live
/// feed: while the dock is open and pinned the glass is of the desktop as it
/// was when it opened. That is the one honest difference from DWM acrylic and
/// it is recorded in docs/TECHNICAL_DECISIONS.md.
/// </summary>
internal static class AcrylicBackdrop
{
    /// <summary>How far down the blur is averaged. Higher is softer.</summary>
    private const int BlurScale = 10;

    /// <summary>Alpha of the tint laid over the blurred desktop, per theme.</summary>
    private const byte TintAlpha = 199;   // 78%

    /// <summary>How much grain the material carries.</summary>
    private const byte NoiseAlpha = 7;

    /// <summary>
    /// Grabs the desktop behind <paramref name="dipRect"/> and turns it into a
    /// frosted brush. Returns null when the screen cannot be read (a locked
    /// session, some remote-desktop setups), in which case the caller keeps its
    /// plain translucent brush.
    /// </summary>
    public static ImageBrush? Capture(Rect dipRect, double scale, Color tint)
    {
        if (dipRect.Width < 4 || dipRect.Height < 4) return null;

        var left = (int)Math.Round(dipRect.Left * scale);
        var top = (int)Math.Round(dipRect.Top * scale);
        var width = (int)Math.Round(dipRect.Width * scale);
        var height = (int)Math.Round(dipRect.Height * scale);

        if (width < 8 || height < 8) return null;

        try
        {
            using var shot = new DBitmap(width, height, DImaging.PixelFormat.Format32bppRgb);
            using (var g = DGraphics.FromImage(shot))
            {
                g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            using var frosted = Frost(shot, tint);

            // A way to look at the material on its own, for the acceptance run.
            if (Environment.GetEnvironmentVariable("PEGGED_ACRYLIC_DUMP") is { Length: > 0 } dump)
            {
                try { frosted.Save(dump, DImaging.ImageFormat.Png); }
                catch (Exception) { }
            }

            return new ImageBrush(ToSource(frosted))
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
            };
        }
        catch (Exception)
        {
            // No screen to read: the plain translucent brush is the fallback.
            return null;
        }
    }

    /// <summary>Blur, tint, grain: the three ingredients of the material.</summary>
    private static DBitmap Frost(DBitmap source, Color tint)
    {
        var smallWidth = Math.Max(4, source.Width / BlurScale);
        var smallHeight = Math.Max(3, source.Height / BlurScale);

        // Down, two box passes, back up. Down-scaling and up-scaling with
        // bilinear is a box blur already; the extra passes on the small image
        // round it into something that reads as frosted rather than smeared.
        using var small = new DBitmap(smallWidth, smallHeight, DImaging.PixelFormat.Format32bppRgb);
        using (var g = DGraphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, new System.Drawing.Rectangle(0, 0, smallWidth, smallHeight));
        }

        BoxPass(small, 2);

        var result = new DBitmap(source.Width, source.Height, DImaging.PixelFormat.Format32bppRgb);
        using (var g = DGraphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(small, new System.Drawing.Rectangle(0, 0, source.Width, source.Height));

            // Tint: Bg.Acrylic at 78% in light, 72% in dark. The surface is
            // opaque, so the brush's alpha blends instead of being ignored.
            using var wash = new System.Drawing.SolidBrush(DColor.FromArgb(TintAlpha, tint.R, tint.G, tint.B));
            g.FillRectangle(wash, 0, 0, source.Width, source.Height);
        }

        Grain(result);
        return result;
    }

    /// <summary>
    /// The grain, added by hand. A TextureBrush was the obvious way and the
    /// wrong one: GDI+ ignores the alpha of a brush drawn onto a 32bpp bitmap,
    /// so the noise came out opaque and buried the whole material.
    /// </summary>
    private static void Grain(DBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
            DImaging.ImageLockMode.ReadWrite, DImaging.PixelFormat.Format32bppRgb);

        try
        {
            var pixels = new int[width * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            var random = new Random(20261018);
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                var delta = random.Next(-NoiseAlpha, NoiseAlpha + 1);

                var r = Math.Clamp(((c >> 16) & 0xFF) + delta, 0, 255);
                var g = Math.Clamp(((c >> 8) & 0xFF) + delta, 0, 255);
                var b = Math.Clamp((c & 0xFF) + delta, 0, 255);

                pixels[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>One separable box pass over a small bitmap, in place.</summary>
    private static void BoxPass(DBitmap bitmap, int radius)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var span = radius * 2 + 1;

        var pixels = new int[width * height];
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
            DImaging.ImageLockMode.ReadWrite, DImaging.PixelFormat.Format32bppRgb);

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            var buffer = new int[pixels.Length];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var sx = Math.Clamp(x + k, 0, width - 1);
                        var c = pixels[y * width + sx];
                        r += (c >> 16) & 0xFF;
                        g += (c >> 8) & 0xFF;
                        b += c & 0xFF;
                    }
                    buffer[y * width + x] = unchecked((int)0xFF000000)
                                            | ((r / span) << 16) | ((g / span) << 8) | (b / span);
                }
            }

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    int r = 0, g = 0, b = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var sy = Math.Clamp(y + k, 0, height - 1);
                        var c = buffer[sy * width + x];
                        r += (c >> 16) & 0xFF;
                        g += (c >> 8) & 0xFF;
                        b += c & 0xFF;
                    }
                    pixels[y * width + x] = unchecked((int)0xFF000000)
                                            | ((r / span) << 16) | ((g / span) << 8) | (b / span);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static BitmapSource ToSource(DBitmap bitmap)
    {
        // Not GetHbitmap(): GDI drops the alpha channel of a 32bpp bitmap on
        // the way to an HBITMAP, and CreateBitmapSourceFromHBitmap then hands
        // back a fully transparent image. Reading the pixels straight out of
        // the bitmap keeps them.
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, DImaging.ImageLockMode.ReadOnly,
            DImaging.PixelFormat.Format32bppRgb);

        try
        {
            var source = BitmapSource.Create(
                bitmap.Width, bitmap.Height, 96, 96,
                PixelFormats.Bgr32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
