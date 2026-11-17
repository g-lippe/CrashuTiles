namespace CrashuTiles;

/// <summary>Tiny append-only log in %APPDATA%\CrashuTiles\crashutiles.log (restarted past 1 MB).</summary>
static class Log
{
    static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppPaths.Folder, "crashutiles.log");

    public static void Info(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Folder);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* logging must never take the app down */ }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }
}

static class AppPaths
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrashuTiles");
    public static string Config => Path.Combine(Folder, "config.json");
}
