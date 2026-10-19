using System;
using System.IO;
using System.Media;

namespace Pegged.Core;

/// <summary>
/// Windows has no equivalent of the system sounds macOS plays, so Pegged
/// synthesises its own: a short bell when a capture hangs, a softer click when
/// one comes down. Built in memory once, played through SoundPlayer, and
/// silent the moment sounds are turned off.
/// </summary>
public static class SoundService
{
    public enum Sound
    {
        Hang,
        TakeDown,
        Trash,
        Copy,
        Error,
    }

    private static readonly Lazy<MemoryStream> HangSound = new(() => Build(static t => HangSamples(t)));
    private static readonly Lazy<MemoryStream> TakeDownSound = new(() => Build(static t => TakeDownSamples(t)));
    private static readonly Lazy<MemoryStream> TrashSound = new(() => Build(static t => TrashSamples(t)));
    private static readonly Lazy<MemoryStream> CopySound = new(() => Build(static t => CopySamples(t)));
    private static readonly Lazy<MemoryStream> ErrorSound = new(() => Build(static t => ErrorSamples(t)));

    public static void Play(Sound sound)
    {
        if (!Settings.Current.Sounds) return;

        var stream = sound switch
        {
            Sound.Hang => HangSound.Value,
            Sound.TakeDown => TakeDownSound.Value,
            Sound.Trash => TrashSound.Value,
            Sound.Copy => CopySound.Value,
            _ => ErrorSound.Value,
        };

        try
        {
            // A MemoryStream can only be read once, and SoundPlayer rewinds it:
            // a fresh player each time keeps it simple.
            stream.Position = 0;
            using var player = new SoundPlayer(stream);
            player.Play();
        }
        catch (Exception)
        {
            // A missing sound must never interrupt the work.
        }
    }

    // ---- synthesis -------------------------------------------------------

    private const int SampleRate = 44100;

    private static double HangSamples(double t)
        => Math.Sin(2 * Math.PI * 2200 * t) * Math.Exp(-18 * t) * 0.35;

    private static double TakeDownSamples(double t)
        => Math.Sin(2 * Math.PI * 900 * t) * Math.Exp(-40 * t) * 0.28;

    private static double TrashSamples(double t)
        => Math.Sin(2 * Math.PI * (420 - 180 * t) * t) * Math.Exp(-7 * t) * 0.30;

    private static double CopySamples(double t)
        => Math.Sin(2 * Math.PI * 1600 * t) * Math.Exp(-28 * t) * 0.24;

    private static double ErrorSamples(double t)
        => Math.Sin(2 * Math.PI * 220 * t) * Math.Exp(-6 * t) * 0.25;

    /// <summary>Writes a 16 bit mono WAV for one of the shapes above.</summary>
    private static MemoryStream Build(Func<double, double> shape)
    {
        var seconds = 0.35;
        var count = (int)(SampleRate * seconds);
        var stream = new MemoryStream();

        // RIFF header, written by hand: no dependency on a wave library.
        Write(stream, "RIFF");
        Write(stream, 36 + count * 2);
        Write(stream, "WAVE");

        Write(stream, "fmt ");
        Write(stream, 16);            // chunk size
        Write(stream, (short)1);      // PCM
        Write(stream, (short)1);      // mono
        Write(stream, SampleRate);
        Write(stream, SampleRate * 2);// byte rate
        Write(stream, (short)2);      // block align
        Write(stream, (short)16);     // bits per sample

        Write(stream, "data");
        Write(stream, count * 2);

        for (var i = 0; i < count; i++)
        {
            var t = i / (double)SampleRate;
            var value = Math.Clamp(shape(t), -1, 1);
            Write(stream, (short)(value * short.MaxValue));
        }

        stream.Position = 0;
        return stream;
    }

    private static void Write(Stream s, string text)
    {
        foreach (var c in System.Text.Encoding.ASCII.GetBytes(text)) s.WriteByte(c);
    }

    private static void Write(Stream s, int value) => s.Write(BitConverter.GetBytes(value), 0, 4);

    private static void Write(Stream s, short value) => s.Write(BitConverter.GetBytes(value), 0, 2);
}
