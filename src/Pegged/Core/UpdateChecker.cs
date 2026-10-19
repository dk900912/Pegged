using System;
using System.Diagnostics;

namespace Pegged.Core;

/// <summary>
/// A no-op updater stub: opens the project page so the user can grab a new
/// build. A real updater would check GitHub Releases, compare semver, and
/// download + relaunch; for the v1.0 port that is more code than is worth
/// the maintenance.
/// </summary>
public static class UpdateChecker
{
    public static void Run()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/dk900912/Pegged/releases",
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // No browser: try the default.
            try { Process.Start("explorer.exe", "https://github.com/dk900912/Pegged/releases"); }
            catch (Exception) { }
        }
    }
}
