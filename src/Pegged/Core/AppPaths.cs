using System;
using System.IO;

namespace Pegged.Core;

/// <summary>
/// Where Pegged keeps its own things: settings, the screenshots it owns and
/// the pictures copied from the clipboard.
/// </summary>
public static class AppPaths
{
    public static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pegged");

    /// <summary>Captures Pegged owns: what you take down goes to the Recycle Bin.</summary>
    public static readonly string ScreenshotsFolder =
        Path.Combine(DataFolder, "Screenshots");

    public static readonly string SettingsFile = Path.Combine(DataFolder, "settings.json");

    /// <summary>Where Windows puts Win+Shift+S captures, when it can be told.</summary>
    public static string DefaultScreenshotFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var screens = Path.Combine(pictures, "Screenshots");
        if (Directory.Exists(screens)) return screens;

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            var od = Path.Combine(oneDrive, "Pictures", "Screenshots");
            if (Directory.Exists(od)) return od;
        }

        return Directory.Exists(pictures) ? pictures : DataFolder;
    }

    /// <summary>The folder captures are watched in: the user's choice, or Windows'.</summary>
    public static string WatchFolder()
    {
        var chosen = Settings.Current.ScreenshotFolder;
        if (!string.IsNullOrWhiteSpace(chosen) && Directory.Exists(chosen)) return chosen;
        return DefaultScreenshotFolder();
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ScreenshotsFolder);
    }
}
