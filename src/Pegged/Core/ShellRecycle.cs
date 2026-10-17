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
            var result = NativeMethods.SHFileOperation(ref op);
            return result == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
