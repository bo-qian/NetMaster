using NetMaster.Core;

namespace NetMaster.Worker;

internal static class Program
{
    private static void Main(string[] args)
    {
        var home = AppStorage.DefaultHome;
        var homeIndex = Array.IndexOf(args, "--home");
        if (homeIndex >= 0 && homeIndex + 1 < args.Length) home = args[homeIndex + 1];
        using var instance = new Mutex(true, @"Local\" + Protocol.PipeName(home), out var acquired);
        if (!acquired) return;
        try
        {
            // StartupTask has no --home argument; migrate previous Windows data
            // before guarding, even when the main window is never opened.
            using var engine = new GuardianEngine(homeIndex >= 0 ? new AppStorage(home) : new AppStorage());
            // Keep mutex ownership on the main thread for the full process lifetime.
            engine.RunAsync().GetAwaiter().GetResult();
        }
        finally { instance.ReleaseMutex(); }
    }
}
