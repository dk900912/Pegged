using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pegged.Core;

/// <summary>
/// Reads a thumbnail for a capture: the image itself, downscaled and frozen so
/// it can be shared between threads, or the Explorer thumbnail for a recording.
/// </summary>
public static class ThumbnailService
{
    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".heic", ".tif", ".tiff", ".gif", ".webp", ".bmp" };

    public static bool IsImage(string path)
        => Array.IndexOf(ImageExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    /// <summary>
    /// A thumbnail no larger than <paramref name="maxPixels"/> on its long side.
    /// Returns null when the file is not a picture it can read, which is also
    /// how a half written file announces itself.
    /// </summary>
    public static BitmapSource? Create(string path, int maxPixels = 320)
    {
        if (!File.Exists(path)) return null;

        if (IsImage(path))
        {
            var image = DecodeImage(path, maxPixels);
            if (image is not null) return image;
        }

        return ShellThumbnail(path, maxPixels);
    }

    private static BitmapSource? DecodeImage(string path, int maxPixels)
    {
        try
        {
            // A read that shares writes never blocks the screenshot tool, and
            // a file still being written simply fails the decode.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            var frame = decoder.Frames[0];
            var scale = Math.Min(1.0, maxPixels / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
            var transform = scale < 1.0 ? new ScaleTransform(scale, scale) : Transform.Identity;

            var resized = new TransformedBitmap(frame, transform);
            var result = BitmapFrame.Create(resized);
            result.Freeze();
            return result;
        }
        catch (Exception)
        {
            // Not decodable: the caller falls back to the shell thumbnail.
            return null;
        }
    }

    /// <summary>
    /// The thumbnail Windows itself would show, which is how a recording gets
    /// its first frame without decoding video in process.
    /// </summary>
    private static BitmapSource? ShellThumbnail(string path, int size)
    {
        try
        {
            var item = NativeShell.SHCreateItemFromParsingName(path);
            if (item is null) return null;

            var factory = (NativeShell.IShellItemImageFactory)item;
            var hr = factory.GetImage(new NativeShell.SIZE { cx = size, cy = size },
                                      0x2 /* SIIGBF_RESIZETOFIT */ | 0x1 /* SIIGBF_ICONONLY */,
                                      out var bitmap);
            if (hr != 0 || bitmap == IntPtr.Zero) return null;

            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeShell.DeleteObject(bitmap);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal static class NativeShell
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem ppv);

    private static readonly Guid ShellItemGuid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public static IShellItemImageFactory? SHCreateItemFromParsingName(string path)
    {
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, ShellItemGuid, out var item);
            return (IShellItemImageFactory)item;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}
