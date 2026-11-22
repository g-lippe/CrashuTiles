namespace CrashuTiles;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--preview") return Preview.Run(args);

        // One instance only: two would fight over the dock.
        using var single = new Mutex(true, @"Local\CrashuTiles.SingleInstance", out bool first);
        if (!first) { Log.Info("Already running; this second copy exits"); return 1; }

        Log.Info($"Starting {Application.ProductVersion} from {Environment.ProcessPath} (pid {Environment.ProcessId})");
        ApplicationConfiguration.Initialize();

        // Every way out leaves a line in the log, so an unexpected exit can be told apart
        // from a crash, a Windows shutdown, or the process being killed (which logs nothing).
        Application.ThreadException += (_, e) => Log.Info("UI error: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info("Fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Info("Background task error: " + e.Exception); e.SetObserved(); };
        Microsoft.Win32.SystemEvents.SessionEnding += (_, e) => Log.Info($"Windows session ending ({e.Reason})");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Info("Process exiting");

        Application.Run(new TrayApp());
        Log.Info("Stopped normally");
        return 0;
    }
}
