using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Pegged.Core;

/// <summary>
/// Reads images off the Windows clipboard and hangs them on the line.
///
/// Pegged never sets the clipboard, only reads it, so there is no loop to
/// break. The privacy filter is also simple: text, files, and everything
/// that is not a picture is ignored.
/// </summary>
public static class ClipboardWatcher
{
    private const string PngFormat = "PNG";
    private const string ClipboardTimestampFileName = "clipboard-yyyyMMdd-HHmmss-ffff.png";

    public static void OnClipboardChanged()
    {
        if (!Settings.Current.WatchClipboard) return;

        // The clipboard is sometimes held by another window for a moment.
        // One quick retry covers the common case.
        IDataObject? data = null;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                data = Clipboard.GetDataObject();
                break;
            }
            catch (Exception)
            {
                System.Threading.Thread.Sleep(40);
            }
        }

        if (data is null || !data.GetDataPresent(PngFormat) && !data.GetDataPresent(DataFormats.Bitmap))
            return;

        try { DispatchOnUi(SaveAndHang, data); }
        catch (Exception)
        {
            SoundService.Play(SoundService.Sound.Error);
        }
    }

    private static void DispatchOnUi(Action<IDataObject> action, IDataObject data)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        if (app.Dispatcher.CheckAccess()) action(data);
        else app.Dispatcher.BeginInvoke(action, data);
    }

    private static void SaveAndHang(IDataObject data)
    {
        BitmapSource? bitmap = null;
        byte[]? rawPng = null;

        if (data.GetDataPresent(PngFormat))
        {
            // The clipboard ships PNGs as a stream; copy it into memory and
            // let WPF decode it the same way as a file.
            if (data.GetData(PngFormat) is MemoryStream pngStream)
            {
                rawPng = pngStream.ToArray();
                bitmap = PngToBitmap(rawPng);
            }
            else if (data.GetData(PngFormat) is Stream other)
            {
                using var ms = new MemoryStream();
                other.CopyTo(ms);
                rawPng = ms.ToArray();
                bitmap = PngToBitmap(rawPng);
            }
        }

        if (bitmap is null && data.GetDataPresent(DataFormats.Bitmap))
        {
            // WPF can also pull a Bitmap directly off the clipboard.
            try
            {
                if (Clipboard.GetImage() is { } bmp) bitmap = bmp;
            }
            catch (Exception) { /* clipboard went away */ }
        }

        if (bitmap is null) return;

        AppPaths.EnsureCreated();
        var name = DateTime.Now.ToString("yyyyMMdd-HHmmss-ffff") + ".png";
        var path = Path.Combine(AppPaths.ScreenshotsFolder, "Clipboard " + name);

        if (rawPng is not null)
        {
            File.WriteAllBytes(path, rawPng);
        }
        else
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        App.CaptureStore.Hang(path, flying: true);
        App.Instance?.Line?.Peek();
    }

    private static BitmapSource? PngToBitmap(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }
        catch (Exception)
        {
            return null;
        }
    }
}
