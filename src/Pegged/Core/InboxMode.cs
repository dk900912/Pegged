using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Pegged.Core;

/// <summary>
/// Inbox mode: Pegged takes over where Windows saves screenshots.
///
/// On the Mac the original moves the system screenshot folder into its own
/// store, so the Desktop only gets what you keep and a capture exists as a
/// file the instant it is taken. The Windows equivalent is the Screenshots
/// known folder: Win+Shift+S and the Snipping Tool both write there, and it
/// can be redirected per user without touching the Pictures library.
///
/// The previous location is remembered before the first redirect and put back
/// when the mode is switched off or Pegged quits, so the system is never left
/// pointing at a folder nobody is watching. If the redirect is refused (a
/// policy, an older build) the mode reports failure and nothing is changed.
/// </summary>
public static class InboxMode
{
    /// <summary>FOLDERID_Screenshots.</summary>
    private static readonly Guid ScreenshotsFolderId = new("B7BEDE81-DF94-4682-A7D8-57A71709C8E0");

    /// <summary>Where Windows is told to put captures while the mode is on.</summary>
    public static string Folder => AppPaths.ScreenshotsFolder;

    /// <summary>
    /// Why the redirect is not available on this system, or null when it is.
    ///
    /// FOLDERID_Screenshots is not a fixed path: the shell only resolves it
    /// once it knows about the folder, and a profile that has never saved a
    /// capture there answers ERROR_FILE_NOT_FOUND (0x80070002). Measured on
    /// this machine: writing the User Shell Folders value by hand,
    /// KF_FLAG_DONT_VERIFY and a SHChangeNotify broadcast all leave it
    /// unresolved, so there is genuinely nothing to redirect and the mode has
    /// to say so up front instead of failing after the switch is flipped.
    /// </summary>
    public static string? Unavailable => _probe.Value;

    /// <summary>The HRESULT of the last failed redirect, for the settings row.</summary>
    public static string? LastError { get; private set; }

    private static readonly Lazy<string?> _probe = new(ProbeSupport);

    private static string? ProbeSupport()
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            var id = ScreenshotsFolderId;
            var hr = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer);
            if (hr == 0) return null;
            return "0x" + ((uint)hr).ToString("X8");
        }
        catch (Exception)
        {
            return "unavailable";
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, string path);

    /// <summary>Where Windows saves screenshots right now, or null.</summary>
    public static string? CurrentSystemFolder()
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            var id = ScreenshotsFolderId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer) != 0) return null;
            return Marshal.PtrToStringUni(pointer);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>True when Windows is already writing into Pegged's folder.</summary>
    public static bool IsApplied()
    {
        var current = CurrentSystemFolder();
        return current is not null && SameFolder(current, Folder);
    }

    /// <summary>
    /// Points Windows at Pegged's folder. Returns false when the system said
    /// no, in which case nothing has changed.
    /// </summary>
    public static bool Apply()
    {
        try
        {
            Directory.CreateDirectory(Folder);

            // Never save our own folder as the "previous" one: after a crash
            // that left the mode applied, the real previous location is the
            // one already recorded.
            if (!IsApplied())
            {
                var current = CurrentSystemFolder();
                if (current is not null && !SameFolder(current, Folder))
                {
                    Settings.Current.InboxPreviousPath = current;
                    Settings.Current.Save();
                }
            }

            var id = ScreenshotsFolderId;
            var hr = SHSetKnownFolderPath(ref id, 0, IntPtr.Zero, Folder);
            LastError = hr == 0 ? null : "0x" + ((uint)hr).ToString("X8");
            return hr == 0;
        }
        catch (Exception)
        {
            LastError = "unavailable";
            return false;
        }
    }

    /// <summary>Puts the system folder back where it was.</summary>
    public static void Restore()
    {
        var previous = Settings.Current.InboxPreviousPath;
        if (string.IsNullOrWhiteSpace(previous)) return;

        try
        {
            if (!Directory.Exists(previous)) Directory.CreateDirectory(previous);
            var id = ScreenshotsFolderId;
            SHSetKnownFolderPath(ref id, 0, IntPtr.Zero, previous);
        }
        catch (Exception)
        {
            // Nothing to undo, or no rights to undo it: leave the setting so
            // the next launch can try again.
            return;
        }

        Settings.Current.InboxPreviousPath = null;
        Settings.Current.Save();
    }

    /// <summary>Switches the mode on or off, and reports whether it stuck.</summary>
    public static bool SetEnabled(bool enabled)
    {
        // Turning it on where the shell has no such folder to give would only
        // flip the switch back a moment later. Refuse before touching either.
        if (enabled && Unavailable is { } why)
        {
            LastError = why;
            return false;
        }

        Settings.Current.InboxEnabled = enabled;

        if (enabled)
        {
            var ok = Apply();
            if (!ok) Settings.Current.InboxEnabled = false;
            Settings.Current.Save();
            App.Instance?.Watch?.Restart();
            return ok;
        }

        Restore();
        Settings.Current.Save();
        App.Instance?.Watch?.Restart();
        return true;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
