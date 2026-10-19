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
            return hr == 0;
        }
        catch (Exception)
        {
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
