using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using DrawingImage = System.Drawing.Image;
using DrawingColor = System.Drawing.Color;
using DrawingRect = System.Drawing.Rectangle;
using DrawingFont = System.Drawing.Font;

namespace Pegged.Core;

/// <summary>
/// The tray menu's 16px icons, drawn when they are first asked for and kept
/// afterwards (showcase 6: "图标 16px 与文本基线对齐").
/// <para>
/// They are drawn rather than shipped so there is no second set of assets to
/// keep in step with the design, and they are drawn in the menu's own text
/// colour so they read on both a light and a dark context menu - a black icon
/// on a dark menu is the one detail that gives the whole menu away.
/// </para>
/// </summary>
internal static class TrayIcons
{
    /// <summary>The Segoe Fluent Icons family that ships with Windows 11, with
    /// the older MDL2 family as the fallback for builders on Windows 10.</summary>
    private static readonly string[] Fonts = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };

    private static readonly Dictionary<(string, DrawingColor), DrawingImage> Cache = new();

    /// <summary>The colour menu text has: near-black on a light menu, white on a
    /// dark one - which is what Windows itself is set to.</summary>
    public static DrawingColor TextColor => ThemeManager.IsDark
        ? DrawingColor.White
        : DrawingColor.FromArgb(0x1A, 0x1B, 0x1F);

    /// <summary>One icon: the glyph at <paramref name="glyph"/>, in the menu text colour.</summary>
    public static DrawingImage Get(string glyph)
    {
        var color = TextColor;
        if (Cache.TryGetValue((glyph, color), out var cached)) return cached;

        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var family = FirstAvailableFont();
            if (family is null) return new Bitmap(16, 16);

            using var font = new DrawingFont(family, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, glyph, font, new DrawingRect(0, 0, 16, 16), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        var icon = new Bitmap(bitmap);
        Cache[(glyph, color)] = icon;
        return icon;
    }

    /// <summary>The first installed family of the two the glyphs live in, or
    /// null on a machine with neither.</summary>
    private static FontFamily? FirstAvailableFont()
    {
        foreach (var name in Fonts)
        {
            try
            {
                var family = new FontFamily(name);
                return family;
            }
            catch (ArgumentException)
            {
                // Not installed: try the older spelling.
            }
        }
        return null;
    }
}
