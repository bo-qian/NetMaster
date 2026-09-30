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
            using var engine = new GuardianEngine(new AppStorage(home));
            // Keep mutex ownership on the main thread for the full process lifetime.
            engine.RunAsync().GetAwaiter().GetResult();
        }
        finally { instance.ReleaseMutex(); }
    }
}
