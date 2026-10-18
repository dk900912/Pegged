using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pegged.Core;

/// <summary>
/// Everything Pegged remembers. Stored as JSON next to the screenshots folder
/// under %APPDATA%\Pegged\settings.json, so it is trivially inspectable and
/// never depends on the registry.
/// </summary>
public sealed class Settings
{
    public const int MaxExtraFolders = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Language tag, or null to follow Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Card size on the line.</summary>
    public CardSize Size { get; set; } = CardSize.Medium;

    /// <summary>How many photos the line keeps, or null for "as many as fit".</summary>
    public int? KeepOnLine { get; set; }

    /// <summary>Tink when something hangs, pop when it comes down.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>Show the line when the pointer rests against the top edge.</summary>
    public bool RevealOnTopEdge { get; set; } = true;

    /// <summary>A new capture shows the line for a moment.</summary>
    public bool PeekAtNewCapture { get; set; } = true;

    /// <summary>Come down even when a full screen app owns the display.</summary>
    public bool ShowOverFullScreenApps { get; set; }

    /// <summary>Hang images copied to the clipboard.</summary>
    public bool WatchClipboard { get; set; }

    /// <summary>Where screenshots land. Null means "let Pegged find out".</summary>
    public string? ScreenshotFolder { get; set; }

    /// <summary>Extra folders watched for new images, three at most.</summary>
    public List<string> ExtraFolders { get; set; } = new();

    /// <summary>Windows saves captures into Pegged's own folder while this is on.</summary>
    public bool InboxEnabled { get; set; }

    /// <summary>Where Windows saved screenshots before the mode was switched on.</summary>
    public string? InboxPreviousPath { get; set; }

    /// <summary>The inbox mode was offered once already.</summary>
    public bool InboxOffered { get; set; }

    /// <summary>Auto / Light / Dark.</summary>
    public ThemeKind Theme { get; set; } = ThemeKind.Auto;

    /// <summary>Seconds the pointer rests at the top edge before the line comes down.</summary>
    public double RevealDelay { get; set; } = 0.25;

    /// <summary>Seconds the pointer is away before the line tucks back up.</summary>
    public double RetractDelay { get; set; } = 0.5;

    /// <summary>The shortcut that shows and hides the line, or null for none.</summary>
    public HotkeyBinding? Shortcut { get; set; } = HotkeyBinding.Default;

    /// <summary>Start with Windows.</summary>
    public bool LaunchAtLogin { get; set; }

    /// <summary>Show the line during the first seconds of the first launch.</summary>
    public bool Welcomed { get; set; }

    /// <summary>Paths that were on the line when Pegged last closed.</summary>
    public List<string> Hanging { get; set; } = new();

    /// <summary>Clipboard watching was offered once already.</summary>
    public bool ClipboardOffered { get; set; }

    // ---- plumbing -------------------------------------------------------

    private static readonly string FilePath =
        Path.Combine(AppPaths.DataFolder, "settings.json");

    public static Settings Current { get; private set; } = Load();

    /// <summary>Raised whenever the settings were written: the line rehangs
    /// itself when the card size or the language changes.</summary>
    public static event EventHandler? Saved;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                if (JsonSerializer.Deserialize<Settings>(text, Options) is { } s)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch (Exception)
        {
            // A broken file must never keep the app from starting.
        }

        var fresh = new Settings();
        fresh.Normalize();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Settings are a convenience, not a requirement.
        }
    }

    private void Normalize()
    {
        ExtraFolders = ExtraFolders.Where(p => !string.IsNullOrWhiteSpace(p))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .Take(MaxExtraFolders)
                                   .ToList();
        Hanging = Hanging.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        RevealDelay = Math.Clamp(RevealDelay, 0.05, 3.0);
        RetractDelay = Math.Clamp(RetractDelay, 0.1, 10.0);
    }

    /// <summary>The language actually used: the chosen one, or Windows'.</summary>
    public CultureInfo EffectiveCulture()
    {
        if (!string.IsNullOrWhiteSpace(Language))
        {
            try { return new CultureInfo(Language); }
            catch (CultureNotFoundException) { }
        }

        return CultureInfo.CurrentUICulture;
    }
}

public enum CardSize
{
    Small,
    Medium,
    Large,
}

public enum ThemeKind
{
    Auto,
    Light,
    Dark,
}

/// <summary>A global shortcut: a virtual key code plus Win32 modifier flags.</summary>
public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public string Display { get; set; } = string.Empty;

    /// <summary>Ctrl+Alt+P, the Windows cousin of Tendedero's Ctrl+Option+T.</summary>
    public static HotkeyBinding Default { get; } = new()
    {
        Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
        VirtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.P),
        Display = "Ctrl+Alt+P",
    };
}
