using System;
using System.IO;

namespace Pegged.Core;

/// <summary>
/// The first few bytes of a file are a tell. Pegged only hangs things that
/// look like a picture or a video; random files that drop in next to a
/// screenshot are ignored.
/// </summary>
public static class MagicNumbers
{
    public static bool IsCaptureable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 16) return false;

            Span<byte> head = stackalloc byte[16];
            if (stream.Read(head) != 16) return false;

            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return true;                          // JPEG
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return true;          // PNG
            if (head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38) return true;          // GIF
            if (head[0] == 0x42 && head[1] == 0x4D) return true;                                                // BMP
            if (head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46 &&
                head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return true;          // WEBP (RIFF...WEBP)

            // MP4 / MOV: "ftyp" at offset 4
            if (head[4] == 0x66 && head[5] == 0x74 && head[6] == 0x79 && head[7] == 0x70) return true;

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
