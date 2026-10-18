using System;
using System.Collections.Generic;
using System.IO;

namespace Pegged.Core;

/// <summary>
/// Owns the per-folder file watchers and rebuilds them whenever the user
/// changes the watched folder list in the settings.
/// </summary>
public sealed class WatchService : IDisposable
{
    private readonly List<ScreenshotWatcher> _watches = new();

    public WatchService()
    {
        Rebuild();
        Settings.Saved += (_, _) => Rebuild();
    }

    public IReadOnlyList<string> Folders
    {
        get
        {
            var list = new List<string>(_watches.Count);
            foreach (var w in _watches) list.Add(w.Folder);
            return list;
        }
    }

    public void Rebuild()
    {
        foreach (var w in _watches) w.Dispose();
        _watches.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUnique(seen, Settings.Current.ScreenshotFolder);
        AddUnique(seen, AppPaths.WatchFolder());

        // Inbox mode sends the system captures into Pegged's own folder, so
        // that folder has to be watched even though it is not the user's.
        if (Settings.Current.InboxEnabled) AddUnique(seen, AppPaths.ScreenshotsFolder);

        foreach (var extra in Settings.Current.ExtraFolders) AddUnique(seen, extra);
    }

    /// <summary>Re-create the watchers. Used by the settings window when the
    /// user picks a new folder: change the setting, then call this.</summary>
    public void Restart()
    {
        Rebuild();
    }

    private void AddUnique(HashSet<string> seen, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var path = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(path)) return;
        if (!seen.Add(path)) return;

        try { _watches.Add(new ScreenshotWatcher(path)); }
        catch (Exception) { /* nothing worth telling the user about */ }
    }

    public void Dispose()
    {
        foreach (var w in _watches) w.Dispose();
        _watches.Clear();
    }
}
