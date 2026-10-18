using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace Pegged.Core;

/// <summary>
/// A drag-and-drop source that hands the file off to any Windows drop target
/// using the standard Shell <c>CF_HDROP</c> format, so receivers see a real
/// "file" and not just text.
///
/// On the way back, the <see cref="Effects"/> property tells the caller
/// whether the user dropped a Copy (keep on the line) or a Move (trash the
/// original here and let the receiver have the bytes).
/// </summary>
public static class DragService
{
    /// <summary>Result of a drag operation.</summary>
    public sealed class Result
    {
        public required DragKind Effects { get; init; }
        public required string? Target { get; init; }
    }

    public enum DragKind
    {
        None,
        Copy,
        Move,
    }

    /// <summary>Starts a drag from a window. Blocks until the user drops.</summary>
    public static Result StartDrag(Window source, IReadOnlyList<string> paths, DragKind defaultEffect = DragKind.Copy)
    {
        if (paths.Count == 0)
            return new Result { Effects = DragKind.None, Target = null };

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        // Offer the raw file-list descriptor too, for receivers that ask
        // specifically. FileDrop covers the usual case; this is a polite
        // extra.
        data.SetData(NativeMethods.CFSTR_FILEDESCRIPTOR, BuildDescriptor(paths));
        data.SetData(NativeMethods.CFSTR_FILECONTENTS, BuildContents(paths));

        // Default effect depends on whether all paths live inside our own
        // screenshots folder (a "move" is fine, since the receiver will
        // already own the file there) or outside (don't trash the user's
        // file unless they explicitly drop with Shift).
        var allOurs = true;
        var watched = AppPaths.WatchFolder();
        foreach (var p in paths)
        {
            if (!p.StartsWith(watched, StringComparison.OrdinalIgnoreCase))
            {
                allOurs = false;
                break;
            }
        }

        var initial = allOurs ? DragKind.Move : defaultEffect;
        var initialEffects = initial == DragKind.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        var allowed = DragDropEffects.Copy | DragDropEffects.Move;

        var raw = DragDrop.DoDragDrop(source, data, allowed);
        var effect = raw switch
        {
            DragDropEffects.Move => DragKind.Move,
            DragDropEffects.Copy => DragKind.Copy,
            _ => initial, // user cancelled → treat as the default we offered
        };

        return new Result { Effects = effect, Target = null };
    }

    private static MemoryStream BuildDescriptor(IReadOnlyList<string> paths)
    {
        // FILEDESCRIPTOR: header + 1..N descriptors. The actual files go via
        // FILECONTENTS (we hand them out as an IStream). For images and small
        // files a CF_HDROP is plenty, but offering the formats keeps
        // Outlook-style receivers happy.
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        bw.Write(paths.Count);
        foreach (var path in paths)
        {
            // FILEDESCRIPTOR (96 bytes in Unicode mode)
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            bw.Write((uint)0x0001);                       // dwFlags = FD_PROGRESSUI
            bw.Write(0u);                                 // clipFormat
            bw.Write((short)0); bw.Write((short)0);       // aspect ratio reserved
            bw.Write(0L); bw.Write(0L); bw.Write(0L);     // pointl size
            bw.Write(0L); bw.Write(0L);                   // file attrs/time
            bw.Write((uint)name.Length + 1);
            for (var i = 0; i < name.Length; i++)
                bw.Write((short)name[i]);
            bw.Write((short)0);
            bw.Write((short)0);
        }
        bw.Flush();
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream BuildContents(IReadOnlyList<string> paths)
    {
        // We just include the first path as bytes; receivers that ask
        // for FILECONTENTS are usually picking up the same image they
        // could have gotten from FileDrop.
        var bytes = File.ReadAllBytes(paths[0]);
        var ms = new MemoryStream(bytes) { Position = 0 };
        return ms;
    }
}
