using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Pegged.Core;

/// <summary>
/// Open or close the "run at login" door: a simple <c>HKCU\...\Run</c>
/// registry value, which is what every "start with Windows" toggle on
/// every other tray app you have ever used is doing.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Value = "Pegged";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Value) is string s && !string.IsNullOrEmpty(s);
    }

    public static void SetEnabled(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (k is null) return;

        if (on)
        {
            // Quote the path so spaces survive. The shell does the same.
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return;
            k.SetValue(Value, $"\"{exe}\" --tray");
        }
        else
        {
            k.DeleteValue(Value, throwOnMissingValue: false);
        }
    }
}
