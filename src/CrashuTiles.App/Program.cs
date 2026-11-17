namespace CrashuTiles;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--preview") return Preview.Run(args);

        // One instance only: two would fight over the dock.
        using var single = new Mutex(true, @"Local\CrashuTiles.SingleInstance", out bool first);
        if (!first) return 1;

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Info("UI error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info("Fatal: " + e.ExceptionObject);
        Application.Run(new TrayApp());
        return 0;
    }
}
