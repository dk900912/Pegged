using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Pegged.Core;

/// <summary>
/// Reads images off the Windows clipboard and hangs them on the line.
///
/// Pegged never sets the clipboard, only reads it. Even so, three guards are
/// kept: the app that put the picture there may have asked every clipboard
/// monitor to ignore it (passwords managers do this), the same picture copied
/// twice should not hang twice, and a half-written PNG must never appear on
/// the line.
/// </summary>
public static class ClipboardWatcher
{
    private const string PngFormat = "PNG";

    /// <summary>Set by password managers and "exclude from clipboard history"
    /// style apps: if present, we must not read the picture at all.</summary>
    private const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing";
    private const string ClipboardViewerIgnore = "Clipboard Viewer Ignore";

    /// <summary>Above this size the clipboard is not a screenshot any more.</summary>
    private const long MaxPngBytes = 64L * 1024 * 1024;

    private static string? _lastHash;

    public static void OnClipboardChanged()
    {
        if (!Settings.Current.WatchClipboard) return;

        // The clipboard is sometimes held by another window for a moment.
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

        if (data is null) return;

        // Privacy filter first: an app that opted out must not be screenshotted
        // into our own folder.
        if (data.GetDataPresent(ExcludeFromMonitor) || data.GetDataPresent(ClipboardViewerIgnore))
            return;

        if (!data.GetDataPresent(PngFormat) && !data.GetDataPresent(DataFormats.Bitmap))
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
            try
            {
                if (Clipboard.GetImage() is { } bmp) bitmap = bmp;
            }
            catch (Exception) { /* clipboard went away */ }
        }

        if (bitmap is null) return;
        if (rawPng is not null && rawPng.LongLength > MaxPngBytes) return;

        // Same picture twice in a row: do not hang a duplicate.
        if (rawPng is not null)
        {
            var hash = Convert.ToHexString(SHA256.HashData(rawPng));
            if (hash == _lastHash) return;
            _lastHash = hash;
        }

        AppPaths.EnsureCreated();
        var name = DateTime.Now.ToString("yyyyMMdd-HHmmss-ffff") + ".png";
        var path = Path.Combine(AppPaths.ScreenshotsFolder, "Clipboard " + name);

        // Write to a temporary name and move it into place: a watcher (ours or
        // anyone else's) must never see a half-written PNG.
        var temp = path + ".part";
        try
        {
            if (rawPng is not null)
            {
                File.WriteAllBytes(temp, rawPng);
            }
            else
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(temp);
                encoder.Save(stream);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
            return;
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
