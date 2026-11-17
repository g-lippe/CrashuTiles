using System.Diagnostics;
using CrashuTiles.Config;

namespace CrashuTiles.Actions;

/// <summary>Runs the actions that leave the deck: launching apps, opening URLs, sending keys.</summary>
static class ActionRunner
{
    public static void Run(ActionConfig a)
    {
        try
        {
            switch (a.Type.ToLowerInvariant())
            {
                case "launch":
                    if (string.IsNullOrWhiteSpace(a.Path)) throw new ArgumentException("launch needs a path");
                    Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(a.Path), a.Args ?? "") { UseShellExecute = true });
                    break;
                case "url":
                    if (string.IsNullOrWhiteSpace(a.Url)) throw new ArgumentException("url needs a url");
                    Process.Start(new ProcessStartInfo(a.Url) { UseShellExecute = true });
                    break;
                case "hotkey":
                    if (string.IsNullOrWhiteSpace(a.Keys)) throw new ArgumentException("hotkey needs keys");
                    KeySender.Send(a.Keys);
                    break;
                case "config":
                    OpenConfig();
                    break;
                default:
                    Log.Info($"Unknown action type '{a.Type}'");
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Info($"Action {a.Type} failed: {e.Message}");
        }
    }

    public static void OpenConfig()
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.Config) { UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", $"\"{AppPaths.Config}\""); } // no app registered for .json
    }
}
