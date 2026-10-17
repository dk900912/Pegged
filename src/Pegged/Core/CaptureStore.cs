using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Pegged.Core;

/// <summary>
/// The line itself: what hangs on it, what leaves it and what is remembered
/// for the next launch. It owns no window and no pixels of its own beyond the
/// thumbnails.
/// </summary>
public sealed class CaptureStore
{
    private readonly object _gate = new();

    public ObservableCollection<LineItem> Items { get; } = new();

    /// <summary>How many captures the line keeps before the oldest falls off.</summary>
    public int MaxItems { get; set; } = 8;

    public bool Sounds
    {
        get => Settings.Current.Sounds;
        set
        {
            Settings.Current.Sounds = value;
            Settings.Current.Save();
        }
    }

    /// <summary>Raised after anything was hung, taken down or reloaded.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with the item that is falling, so the flight can be drawn.</summary>
    public event EventHandler<LineItem>? Falling;

    public int LiveCount
    {
        get { lock (_gate) return Items.Count(i => !i.Falling); }
    }

    /// <summary>Hangs a capture. Returns the item, or null when the file is not
    /// a picture (yet).</summary>
    public LineItem? Hang(string path, bool flying = false, bool quietly = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        lock (_gate)
        {
            if (Items.Any(i => !i.Falling && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                return null;
        }

        var thumb = ThumbnailService.Create(path);
        if (thumb is null) return null;

        var item = new LineItem(path, thumb) { Flying = flying };

        lock (_gate)
        {
            Items.Add(item);
            TrimLocked();
        }

        if (!quietly) SoundService.Play(SoundService.Sound.Hang);
        Save();
        OnChanged();
        return item;
    }

    /// <summary>Starts taking a capture down; the caller animates the fall.</summary>
    public void Drop(Guid id, bool quietly = false)
    {
        LineItem? item;
        lock (_gate)
        {
            item = Items.FirstOrDefault(i => i.Id == id);
            if (item is null || item.Falling) return;
            item.Falling = true;
        }

        Falling?.Invoke(this, item);
        if (!quietly) SoundService.Play(SoundService.Sound.TakeDown);

        // The card is gone from the line straight away; the animation that
        // carries it off the screen runs on its own.
        System.Threading.Tasks.Task.Delay(600).ContinueWith(_ =>
        {
            lock (_gate) Items.Remove(item);
            Save();
            OnChanged();
        }, System.Threading.Tasks.TaskScheduler.Current);
    }

    /// <summary>Takes everything down, one after another.</summary>
    public void Clear()
    {
        List<LineItem> live;
        lock (_gate) live = Items.Where(i => !i.Falling).ToList();

        for (var n = 0; n < live.Count; n++)
        {
            var item = live[n];
            var quiet = n > 0;
            var delay = 60 * n;
            System.Threading.Tasks.Task.Delay(delay).ContinueWith(_ => Drop(item.Id, quietly: quiet),
                System.Threading.Tasks.TaskScheduler.Current);
        }
    }

    /// <summary>Everything still on the line, oldest first.</summary>
    public List<LineItem> Live()
    {
        lock (_gate) return Items.Where(i => !i.Falling).ToList();
    }

    public LineItem? Find(Guid id)
    {
        lock (_gate) return Items.FirstOrDefault(i => i.Id == id);
    }

    public LineItem? FindByPath(string path)
    {
        lock (_gate) return Items.FirstOrDefault(i =>
            !i.Falling && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Captures whose file was moved away, deleted by another app, or
    /// dropped into a folder fall off by themselves.</summary>
    public void Prune()
    {
        var gone = Live().Where(i => !File.Exists(i.Path)).ToList();
        foreach (var item in gone) Drop(item.Id, quietly: true);
    }

    /// <summary>Takes down everything on one side of a capture.</summary>
    public void TakeDownBeside(Guid id, bool toTheLeft)
    {
        var live = Live();
        var index = live.FindIndex(i => i.Id == id);
        if (index < 0) return;

        var side = toTheLeft ? live.Take(index) : live.Skip(index + 1);
        foreach (var item in side.ToList()) Discard(item.Id, quietly: true);
    }

    /// <summary>Moves the file to the Recycle Bin and takes it off the line.</summary>
    public bool Recycle(Guid id, bool quietly = false)
    {
        var item = Find(id);
        if (item is null) return false;

        var ok = ShellRecycle.Send(item.Path);
        if (!ok)
        {
            SoundService.Play(SoundService.Sound.Error);
            return false;
        }

        if (!quietly) SoundService.Play(SoundService.Sound.Trash);
        Drop(id, quietly: true);
        return true;
    }

    /// <summary>The corner cross and "take down" both end up here: a file in
    /// Pegged's own folder is discarded, one anywhere else only leaves the
    /// line.</summary>
    public void Discard(Guid id, bool quietly = false)
    {
        var item = Find(id);
        if (item is null) return;

        if (item.IsInPeggedFolder) Recycle(id, quietly);
        else Drop(id, quietly);
    }

    /// <summary>After an edit, the capture on the line shows the new version.</summary>
    public void ReloadThumbnail(string path)
    {
        var item = FindByPath(path);
        if (item is null) return;

        var thumb = ThumbnailService.Create(path);
        if (thumb is not null) item.Thumb = thumb;
        OnChanged();
    }

    public void ReloadAllThumbnails()
    {
        foreach (var item in Live())
        {
            var thumb = ThumbnailService.Create(item.Path);
            if (thumb is not null) item.Thumb = thumb;
        }

        OnChanged();
    }

    /// <summary>The oldest fall off the end when the line is full.</summary>
    private void TrimLocked()
    {
        while (LiveCount > MaxItems)
        {
            var oldest = Items.FirstOrDefault(i => !i.Falling);
            if (oldest is null) break;
            LetGoLocked(oldest);
        }
    }

    private void LetGoLocked(LineItem item)
    {
        if (item.IsInPeggedFolder && ShellRecycle.Send(item.Path))
        {
            item.Falling = true;
            Falling?.Invoke(this, item);
            var captured = item;
            System.Threading.Tasks.Task.Delay(600).ContinueWith(_ =>
            {
                lock (_gate) Items.Remove(captured);
                OnChanged();
            }, System.Threading.Tasks.TaskScheduler.Current);
            return;
        }

        item.Falling = true;
        Falling?.Invoke(this, item);
        var falling = item;
        System.Threading.Tasks.Task.Delay(600).ContinueWith(_ =>
        {
            lock (_gate) Items.Remove(falling);
            OnChanged();
        }, System.Threading.Tasks.TaskScheduler.Current);
    }

    // ---- persistence -----------------------------------------------------

    public void Save()
    {
        List<string> paths;
        lock (_gate) paths = Items.Where(i => !i.Falling).Select(i => i.Path).ToList();

        Settings.Current.Hanging = paths;
        Settings.Current.Save();
    }

    /// <summary>Everything comes back as it was. Thumbnails are made here, off
    /// the animation path, so a launch is instant even with a full line.</summary>
    public void Restore()
    {
        foreach (var path in Settings.Current.Hanging.ToList())
        {
            if (!File.Exists(path)) continue;

            var thumb = ThumbnailService.Create(path);
            if (thumb is null) continue;

            lock (_gate)
            {
                if (Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                Items.Add(new LineItem(path, thumb));
            }
        }

        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
