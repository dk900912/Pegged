using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace trayprobe;

/// <summary>
/// Drives the *shipped* Pegged tray service in a scratch process.
///
/// The real menu cannot be opened through synthetic input in this session:
/// injected mouse events never reach the Windows shell taskbar here (not even
/// the Start button highlights on hover), so a shell-level probe would prove
/// nothing. This loads Pegged.dll, wires just enough of App for the service to
/// construct, opens the real ContextMenuStrip - which also runs the
/// Opening/RefreshMenu path - and prints the item tree, the ticks, the
/// shortcut text and the MDL2 glyph bitmaps, then photographs the menu.
///
/// Why not call ThemeManager.Apply: it merges a pack URI dictionary that
/// resolves against the entry assembly, and here the entry assembly is this
/// probe, not Pegged. IsDark is set directly instead; nothing else in the tray
/// path touches the dictionaries.
/// </summary>
internal static class Program
{
    private const string Publish = @".";
    private const string Shots = @"..\..\..\outputs\acceptance\shots\audit";

    [STAThread]
    private static void Main(string[] args)
    {
        // The console is GBK here; printed through it, every label comes out
        // as question marks and two translations are indistinguishable.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected */ }
        var publish = Path.GetFullPath(Publish);
        var shots = Path.GetFullPath(Shots);
        Directory.CreateDirectory(shots);

        if (Environment.GetEnvironmentVariable("TRAYPROBE_DUMP_FIELDS") == "1")
        {
            FieldsProbe.Dump();
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            var path = Path.Combine(publish, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        // publish/Pegged.exe is a native apphost stub; the managed assembly is
        // Pegged.dll. Loading the exe throws BadImageFormatException.
        var pegged = Assembly.LoadFrom(Path.Combine(publish, "Pegged.dll"));
        var appType = pegged.GetType("Pegged.App")!;
        var settingsType = pegged.GetType("Pegged.Core.Settings")!;
        var locType = pegged.GetType("Pegged.Localization.Loc")!;
        var storeType = pegged.GetType("Pegged.Core.CaptureStore")!;
        var themeType = pegged.GetType("Pegged.Core.ThemeManager")!;

        var current = settingsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);

        // Optional: a language tag to re-apply before building the menu.
        var language = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (language is not null)
        {
            settingsType.GetProperty("Language")!.SetValue(current, language);
            locType.GetMethod("Apply")!.Invoke(null, new[] { current });
        }

        var dark = args.Contains("--dark");
        themeType.GetProperty("IsDark", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                 .SetValue(null, dark);

        // TrayService's constructor reaches for App.CaptureStore, which is
        // Application.Current cast to Pegged.App. Running the real Application
        // constructor is not an option: it queues OnStartup, which then runs
        // when the menu is pumped and tries to build the whole line (and dies
        // on a pack URI that resolves against this probe's assembly). So: an
        // App with no constructor at all, dropped into Application's private
        // _appInstance, with a real CaptureStore behind it.
        var app = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(appType);
        var instanceField = typeof(System.Windows.Application).GetField("_appInstance",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        if (instanceField is null)
        {
            Console.WriteLine("NO _appInstance field: " + string.Join(", ",
                typeof(Application).GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                                   .Select(f => f.Name)));
            return;
        }
        instanceField.SetValue(null, app);

        var store = Activator.CreateInstance(storeType)!;
        var storeField = appType.GetField("<Store>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? appType.GetField("Store", BindingFlags.Instance | BindingFlags.NonPublic)!;
        storeField.SetValue(app, store);

        var trayType = pegged.GetType("Pegged.Core.TrayService")!;
        var tray = Activator.CreateInstance(trayType)!;

        var menu = (ContextMenuStrip)trayType.GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                                 .Invoke(tray, null)!;

        Cursor.Position = new Point(700, 320);
        menu.Show(Point.Empty);           // Opening -> RefreshMenu() runs here.

        var lines = new List<string>();
        Walk(menu.Items, lines);

        Pump(700);
        var bounds = menu.Bounds;
        var name = language is null ? (dark ? "tray-menu-dark" : "tray-menu") : "tray-menu-" + language;
        if (dark && language is not null) name = "tray-menu-" + language + "-dark";
        var shot = Path.Combine(shots, name + ".png");
        using (var bmp = new Bitmap(bounds.Width, bounds.Height))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            bmp.Save(shot);
        }

        Console.WriteLine("LANG " + (language ?? "(settings)") + " DARK " + (dark ? "yes" : "no"));
        Console.WriteLine("SIZE " + bounds.Width + "x" + bounds.Height + "  -> " + shot);
        // Also written to a file: the console here is GBK and cannot distinguish
        // two Chinese translations, but the file is plain UTF-8.
        File.WriteAllLines(Path.Combine(shots, name + ".txt"), lines);
        foreach (var line in lines)
        {
            Console.WriteLine("  " + line);
        }

        menu.Close();
        ((IDisposable)tray).Dispose();
    }

    private static void Pump(int ms)
    {
        var until = Environment.TickCount + ms;
        while (Environment.TickCount < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Walk(ToolStripItemCollection items, List<string> outLines, string indent = "")
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripSeparator)
            {
                outLines.Add(indent + "----");
                continue;
            }

            if (item is not ToolStripMenuItem mi)
            {
                outLines.Add(indent + "(info) " + item.Text);
                continue;
            }

            var flags = "";
            if (mi.Checked) flags += " [tick]";
            if (!mi.Enabled) flags += " [off]";
            // ShortcutKeyDisplayString is null, not "", on items that never had
            // one set - the getter does not fall back to the ShortcutKeys.
            if (!string.IsNullOrEmpty(mi.ShortcutKeyDisplayString))
                flags += " key=" + mi.ShortcutKeyDisplayString;
            flags += " icon=" + Glyph(mi.Image);
            outLines.Add(indent + mi.Text + flags);

            Walk(mi.DropDownItems, outLines, indent + "    ");
        }
    }

    /// <summary>A 14x14 ink map of one menu row's glyph, so a missing MDL2 glyph
    /// (a hollow tofu box) is visibly a box and not a picture.</summary>
    private static string Glyph(Image? image)
    {
        if (image is not Bitmap bmp) return "none";
        var ink = 0;
        var sb = new System.Text.StringBuilder();
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                var on = p.A > 128;
                sb.Append(on ? '#' : '.');
                if (on) ink++;
            }
            sb.Append('|');
        }
        return "ink " + ink + " " + sb;
    }
}
