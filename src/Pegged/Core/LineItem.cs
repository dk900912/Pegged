using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Pegged.Core;

/// <summary>
/// One capture hanging on the line. The file never moves: the line is only a
/// view onto it.
/// </summary>
public sealed class LineItem : INotifyPropertyChanged, IEquatable<LineItem>
{
    private BitmapSource? _thumb;
    private bool _falling;
    private bool _flying;
    private bool _copied;
    private bool _dragging;
    private bool _pressed;
    private bool _hovering;
    private double _tilt;
    private double _swing;

    public LineItem(string path, BitmapSource? thumb)
    {
        Path = path;
        _thumb = thumb;
        Id = Guid.NewGuid();
        // Every capture hangs a little crooked, like on a real line.
        Tilt = Random.Shared.NextDouble() * 5.0 - 2.5;
    }

    public Guid Id { get; }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>A screen recording rather than a screenshot.</summary>
    public bool IsRecording
    {
        get
        {
            var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            return ext is ".mp4" or ".mov" or ".wmv" or ".avi" or ".webm";
        }
    }

    /// <summary>Whether the file lives in Pegged's own folder: those go to the
    /// Recycle Bin when taken down, or the folder would fill up.
    ///
    /// Compared with a trailing separator: a bare StartsWith made
    /// "...\ScreenshotsEvil\x.png" look like one of ours and sent the user's
    /// file to the Recycle Bin.</summary>
    public bool IsInPeggedFolder
    {
        get
        {
            var folder = AppPaths.ScreenshotsFolder
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            return Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
    }

    public BitmapSource? Thumb
    {
        get => _thumb;
        set { _thumb = value; OnPropertyChanged(); }
    }

    /// <summary>On its way down, off the end of the line or into the bin.</summary>
    public bool Falling
    {
        get => _falling;
        set { _falling = value; OnPropertyChanged(); }
    }

    /// <summary>Still flying in from where it was captured.</summary>
    public bool Flying
    {
        get => _flying;
        set { _flying = value; OnPropertyChanged(); }
    }

    public bool Copied
    {
        get => _copied;
        set { _copied = value; OnPropertyChanged(); }
    }

    public bool Dragging
    {
        get => _dragging;
        set { _dragging = value; OnPropertyChanged(); }
    }

    public bool Pressed
    {
        get => _pressed;
        set { _pressed = value; OnPropertyChanged(); }
    }

    public bool Hovering
    {
        get => _hovering;
        set { _hovering = value; OnPropertyChanged(); }
    }

    /// <summary>The resting tilt, in degrees.</summary>
    public double Tilt
    {
        get => _tilt;
        set { _tilt = value; OnPropertyChanged(); }
    }

    /// <summary>How far the card is swinging right now, on top of its tilt.</summary>
    public double Swing
    {
        get => _swing;
        set { _swing = value; OnPropertyChanged(); }
    }

    public double Aspect
    {
        get
        {
            if (_thumb is null || _thumb.PixelHeight <= 0) return 16.0 / 10.0;
            return (double)_thumb.PixelWidth / _thumb.PixelHeight;
        }
    }

    public bool Equals(LineItem? other) => other is not null && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as LineItem);

    public override int GetHashCode() => Id.GetHashCode();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
