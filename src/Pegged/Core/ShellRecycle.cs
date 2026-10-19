using System;
using System.IO;

namespace Pegged.Core;

/// <summary>
/// Sends a file to the Recycle Bin instead of deleting it, the way a drag to
/// the bin in Explorer behaves.
/// </summary>
public static class ShellRecycle
{
    public static bool Send(string path)
    {
        if (!File.Exists(path)) return false;

        var op = new NativeMethods.SHFILEOPSTRUCT
        {
            wFunc = NativeMethods.FO_DELETE,
            pFrom = NativeMethods.DoubleNullTerminate(Path.GetFullPath(path)),
            fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION
                     | NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI,
        };

        try
        {
            NativeMethods.SHFileOperation(ref op);

            // Whether it worked is decided by where the file is, not by what
            // the call reports. SHFileOperation returns 0 and still sets
            // fAnyOperationsAborted on an ordinary successful delete often
            // enough that trusting it meant "the file went to the bin but the
            // app thought it had failed": an error sound, and the card left
            // hanging until the file watcher noticed the file was gone.
            return !File.Exists(path);
        }
        catch (Exception)
        {
            return !File.Exists(path);
        }
    }
}
