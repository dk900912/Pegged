using System;

namespace Pegged.Core;

/// <summary>
/// The measurements of the line: how big the cards hang, how far apart, how
/// tall the strip is and where the rope runs. All of it follows the card size
/// the user chose, and none of it is hard coded anywhere else.
/// </summary>
public static class LayoutMetrics
{
    public static double ScaleFor(CardSize size) => size switch
    {
        CardSize.Small => 0.8,
        CardSize.Large => 1.35,
        _ => 1.0,
    };

    /// <summary>Applied to whatever the user picked.</summary>
    public static double Scale => ScaleFor(Settings.Current.Size);

    public static double PhotoMaxHeight => Math.Round(104 * Scale);

    public static double CardWidth => Math.Round(150 * Scale);

    public static double Spacing => CardWidth + 24;

    public static double PanelHeight => PhotoMaxHeight + 106;

    public static double RopeTop => 10;

    public static double PinAbove => 9.5;

    public static double PhotoInset => 4;

    public static double CardRadius => 16;

    public static double PhotoRadius => 12;

    /// <summary>The rope hangs as a parabola: deeper on a wide screen, never
    /// more than 30 units.</summary>
    public static double Sag(double width) => Math.Min(30, width * 0.018);

    /// <summary>Where the rope runs at a given distance across the screen.</summary>
    public static double RopeY(double x, double width)
    {
        if (width <= 0) return RopeTop;
        var f = x / width;
        return RopeTop + 4 * Sag(width) * f * (1 - f);
    }

    /// <summary>Where a card hangs. The newest is always on the right; with
    /// more cards than fit the line scrolls towards the older ones.</summary>
    public static double XOf(int index, int count, double width, int visible, double scroll)
    {
        if (Settings.Current.KeepOnLine is not null && count > visible)
        {
            var newest = width / 2 + (visible - 1) * Spacing / 2;
            return newest - (count - 1 - index) * Spacing + scroll;
        }

        var gaps = Math.Max(count - 1, 0);
        var step = gaps > 0 ? Math.Min(Spacing, Math.Max(0, width - 200) / gaps) : Spacing;
        return width / 2 - gaps * step / 2 + index * step;
    }

    /// <summary>How many cards fit across a screen.</summary>
    public static int CapacityFor(double width)
    {
        var usable = width - 200;
        return Math.Clamp((int)(usable / Spacing), 3, 12);
    }
}
