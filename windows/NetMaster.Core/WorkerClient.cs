using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NetMaster.Core;

public sealed class WorkerClient(string home, string executable)
{
    private readonly SemaphoreSlim launchGate = new(1, 1);
    public async Task<Reply> SendAsync(Command command, CancellationToken ct = default, bool allowLaunch = true)
    {
        await launchGate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var pipe = new NamedPipeClientStream(".", Protocol.PipeName(home), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(300, timeout.Token); }
            catch (TimeoutException)
            {
                if (!allowLaunch) throw new IOException("后台进程未运行。");
                if (!File.Exists(executable)) throw new FileNotFoundException("后台程序缺失，请重新安装软件。");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
                start.ArgumentList.Add("--home"); start.ArgumentList.Add(home); Process.Start(start)?.Dispose();
                await pipe.ConnectAsync(10000, timeout.Token);
            }
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(command, Protocol.Json));
            var response = await Protocol.ReadMessageAsync(reader, timeout.Token);
            if (response is null) throw new IOException("后台响应无效。");
            return JsonSerializer.Deserialize<Reply>(response, Protocol.Json) ?? throw new IOException("后台响应无效。");
        }
        finally { launchGate.Release(); }
    }
}
