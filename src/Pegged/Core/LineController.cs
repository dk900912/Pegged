using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Pegged.Views;

namespace Pegged.Core;

/// <summary>
/// The brain behind the line. It watches where the pointer is, decides which
/// monitor the line belongs to, counts how long the pointer rests against the
/// top edge and how long it has been away, then asks the line to come down or
/// tuck itself back up.
///
/// There is one line window that travels between monitors, and one breathing
/// pill per monitor so the line is announced everywhere at once.
/// </summary>
public sealed class LineController : IDisposable
{
    private readonly DispatcherTimer _poll;
    private readonly LineWindow _line;
    private readonly Dictionary<IntPtr, EdgePillWindow> _pills = new();

    private double _dwell;      // seconds resting on the top edge
    private double _away;       // seconds since the pointer left the line
    private bool _pinnedOpen;
    private DateTime? _peekUntil;
    private bool _disposed;

    public LineController()
    {
        MonitorService.Refresh();

        _line = new LineWindow();
        _line.Cleared += (_, _) => App.CaptureStore.Clear();
        _line.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        // 30 Hz is plenty for a pointer that is being deliberately rested
        // against an edge, and it costs nothing.
        _poll = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _poll.Tick += OnTick;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        App.CaptureStore.Changed += (_, _) => _line.OnCardsChanged();
    }

    /// <summary>Asked when the line's own settings button is pressed.</summary>
    public event EventHandler? SettingsRequested;

    public LineWindow Line => _line;

    // ---- lifecycle ------------------------------------------------------

    /// <summary>Shows the line's resting places and starts watching.</summary>
    public void Start()
    {
        var primary = MonitorService.All[0];
        _line.PlaceOn(primary);
        _line.SnapConcealed();
        _line.Show();

        SyncPills();
        _poll.Start();

        if (!Settings.Current.Welcomed)
        {
            Settings.Current.Welcomed = true;
            Settings.Current.Save();
            Peek(4.0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _poll.Stop();
        _poll.Tick -= OnTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        foreach (var pill in _pills.Values)
        {
            pill.Close();
        }
        _pills.Clear();

        _line.Close();
    }

    // ---- the pills ------------------------------------------------------

    private void SyncPills()
    {
        var seen = new HashSet<IntPtr>();

        foreach (var monitor in MonitorService.All)
        {
            seen.Add(monitor.Handle);

            if (!_pills.TryGetValue(monitor.Handle, out var pill))
            {
                pill = new EdgePillWindow();
                _pills[monitor.Handle] = pill;
                pill.PlaceOn(monitor);
                pill.Show();
            }
            else
            {
                pill.PlaceOn(monitor);
            }
        }

        var stale = new List<IntPtr>();
        foreach (var key in _pills.Keys)
            if (!seen.Contains(key)) stale.Add(key);

        foreach (var key in stale)
        {
            _pills[key].Close();
            _pills.Remove(key);
        }
    }

    private void SetPillsVisible(bool visible)
    {
        foreach (var pill in _pills.Values) pill.SetVisible(visible);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        MonitorService.Refresh();
        SyncPills();

        var current = _line.Monitor;
        var home = current is null ? MonitorService.All[0] : Find(current.Handle) ?? MonitorService.All[0];
        _line.PlaceOn(home);

        if (!_line.Revealed) _line.SnapConcealed();
    }

    private MonitorDescription? Find(IntPtr handle)
    {
        foreach (var m in MonitorService.All)
            if (m.Handle == handle) return m;

        return null;
    }

    // ---- the polling loop -----------------------------------------------

    private void OnTick(object? sender, EventArgs e)
    {
        var dt = _poll.Interval.TotalSeconds;
        var settings = Settings.Current;
        NativeMethods.GetCursorPos(out var raw);
        var cursor = new Point(raw.X, raw.Y);

        // A peek from a new capture or the welcome wins over everything.
        if (_peekUntil is { } until)
        {
            if (DateTime.UtcNow >= until)
            {
                _peekUntil = null;
                if (!_pinnedOpen) Retract();
            }
            else
            {
                return;
            }
        }

        if (_pinnedOpen || _line.Pinned)
        {
            _dwell = 0;
            _away = 0;
            return;
        }

        var onLine = _line.Revealed && OverLine(cursor);

        MonitorDescription? band = null;
        if (!onLine && settings.RevealOnTopEdge && MonitorService.IsInHotBand(cursor, out var hot))
        {
            if (AllowedOverFullScreen(hot)) band = hot;
        }

        if (band is not null)
        {
            _away = 0;
            _dwell += dt;

            if (!_line.Revealed && _dwell >= settings.RevealDelay)
                Reveal(band, pinned: false);
            else if (_line.Revealed && _line.Monitor?.Handle != band.Handle)
                _line.PlaceOn(band);
        }
        else if (onLine)
        {
            _dwell = 0;
            _away = 0;
        }
        else
        {
            _dwell = 0;
            _away += dt;

            if (_line.Revealed && _away >= settings.RetractDelay) Retract();
        }
    }

    /// <summary>The pointer is inside the strip, or close enough under it that
    /// the line should not run away while you are reaching for a card.</summary>
    private bool OverLine(Point cursor)
    {
        var r = _line.CurrentRect;
        r.Inflate(0, 24);
        return r.Contains(cursor);
    }

    private bool AllowedOverFullScreen(MonitorDescription monitor)
    {
        if (Settings.Current.ShowOverFullScreenApps) return true;
        return !MonitorService.IsFullScreen(monitor);
    }

    // ---- commands -------------------------------------------------------

    /// <summary>Brings the line down on a monitor.</summary>
    public void Reveal(MonitorDescription? monitor = null, bool pinned = false)
    {
        monitor ??= MonitorService.MonitorAtCursor(out _);

        if (_line.Monitor?.Handle != monitor.Handle || !_line.Revealed)
            _line.PlaceOn(monitor);

        _line.Reveal(pinned);
        SetPillsVisible(false);

        _dwell = 0;
        _away = 0;
    }

    /// <summary>Tucks the line back up.</summary>
    public void Retract()
    {
        _line.Conceal();
        SetPillsVisible(true);

        _dwell = 0;
        _away = 0;
    }

    /// <summary>Shows the line for a moment, then lets it go back to sleep.
    /// Used when a new capture lands and on the very first launch.</summary>
    public void Peek(double seconds = 1.6)
    {
        if (_line.Revealed && _line.Pinned) return;

        Reveal(pinned: false);
        _peekUntil = DateTime.UtcNow.AddSeconds(seconds);
    }

    /// <summary>The shortcut: pin the line down, or let it go.</summary>
    public void TogglePin()
    {
        if (_pinnedOpen)
        {
            _pinnedOpen = false;
            _peekUntil = null;
            Retract();
        }
        else
        {
            _pinnedOpen = true;
            Reveal(pinned: true);
        }
    }

    public bool IsOpen => _line.Revealed;
}
