using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pegged.Core;

/// <summary>
/// Watches a folder for new captures and hangs them on the line.
///
/// Windows screenshots arrive in two waves: the file is created with zero
/// bytes while the snipping tool is still writing to it, and the file may
/// be locked. The retry loop waits for it to settle, doubling the pause each
/// time, then peeks at the magic bytes before doing anything.
/// </summary>
public sealed class ScreenshotWatcher : IDisposable
{
    private readonly FileSystemWatcher _fs;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _folder;

    public ScreenshotWatcher(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);

        _fs = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _fs.Created += (_, e) => _pending.Enqueue(e.FullPath);
        _fs.Renamed += (_, e) => _pending.Enqueue(e.FullPath);
        _fs.EnableRaisingEvents = true;

        _ = Task.Run(() => PumpAsync(_cts.Token));
    }

    public string Folder => _folder;

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_pending.TryDequeue(out var path))
            {
                await TryHangAsync(path, ct);
            }
            else
            {
                try { await Task.Delay(250, ct); }
                catch (TaskCanceledException) { return; }
            }
        }
    }

    private async Task TryHangAsync(string path, CancellationToken ct)
    {
        // Backoff until the file is openable and non-empty.
        for (var attempt = 0; attempt < 12 && !ct.IsCancellationRequested; attempt++)
        {
            if (WaitReady(path))
            {
                if (MagicNumbers.IsCaptureable(path))
                {
                    App.CaptureStore.Hang(path, flying: true);
                    if (App.Instance?.Line is { } line) line.Peek();
                }
                return;
            }

            var delay = Math.Min(40 << Math.Min(attempt, 7), 4000);
            try { await Task.Delay(delay, ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    private static bool WaitReady(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _fs.EnableRaisingEvents = false;
        _fs.Dispose();
    }
}
