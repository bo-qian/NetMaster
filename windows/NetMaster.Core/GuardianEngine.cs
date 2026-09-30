using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace NetMaster.Core;

public sealed class GuardianEngine : IDisposable
{
    private readonly AppStorage storage;
    private readonly LogStore log;
    private readonly INetworkService network;
    private readonly Func<bool> legacyTaskRunning;
    private readonly bool listenForNetworkChanges;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly CancellationTokenSource lifetime = new();
    private volatile CancellationTokenSource? active;
    private NetworkResult lastNetwork = new("unknown", DateTimeOffset.Now, "尚未检测");
    private string guardian = "未配置";
    private bool rejected;
    private DateTimeOffset manualUntil, retryAt;
    private long lastClientTicks = DateTimeOffset.UtcNow.UtcTicks;
    private int failures, authSamples;
    private readonly Dictionary<string, Reply> replies = new();
    private volatile Snapshot published;
    public GuardianEngine(AppStorage storage, INetworkService? network = null, Func<bool>? legacyTaskRunning = null, bool listenForNetworkChanges = true)
    {
        this.storage = storage; log = new(storage);
        this.network = network ?? new NetworkService();
        this.legacyTaskRunning = legacyTaskRunning ?? StartupRegistration.LegacyTaskRunning;
        this.listenForNetworkChanges = listenForNetworkChanges;
        published = State;
    }
    private void Wake() { try { if (wake.CurrentCount == 0) wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } }
    private void NetworkChanged(object? sender, EventArgs args) => Wake();
    private void PublishState() => published = State;
    private Snapshot State => new() { Network = lastNetwork, Guardian = storage.Profile is null ? "未配置" : !storage.Settings.AutoReconnect ? "已暂停" : DateTimeOffset.UtcNow < manualUntil ? "手动操作中，自动重连暂缓" : rejected ? "等待重新登录" : guardian, HasProfile = storage.Profile is not null, Account = Protocol.Mask(storage.Profile?.Account ?? ""), Settings = storage.Settings, DataDirectory = storage.DataDirectory, StorageError = storage.Error ?? log.LastError };
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() => lifetime.Cancel());
        if (listenForNetworkChanges) NetworkChange.NetworkAddressChanged += NetworkChanged;
        log.Write("信息", "后台", "worker.start", "后台进程已启动。");
        var loop = LoopAsync();
        var clients = new List<Task>();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                clients.RemoveAll(task => task.IsCompleted);
                if (clients.Count >= 16) { await Task.WhenAny(clients).WaitAsync(lifetime.Token); continue; }
                var pipe = new NamedPipeServerStream(Protocol.PipeName(storage.Home), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(lifetime.Token); clients.Add(ServeAsync(pipe)); }
                catch { pipe.Dispose(); if (lifetime.IsCancellationRequested) break; throw; }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (listenForNetworkChanges) NetworkChange.NetworkAddressChanged -= NetworkChanged;
            lifetime.Cancel(); await loop; await Task.WhenAll(clients);
            log.Write("信息", "后台", "worker.stop", "后台进程已停止。");
        }
    }
    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(50));
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var line = await Protocol.ReadMessageAsync(reader, timeout.Token);
                if (line is null) return;
                var command = JsonSerializer.Deserialize<Command>(line, Protocol.Json) ?? throw new InvalidDataException();
                if (!Guid.TryParseExact(command.Id, "N", out _)) throw new InvalidDataException();
                Interlocked.Exchange(ref lastClientTicks, DateTimeOffset.UtcNow.UtcTicks);
                if (command.Version == Protocol.Version && command.Name == "status")
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new Reply(true, "", published with { Heartbeat = DateTimeOffset.UtcNow }), Protocol.Json));
                    return;
                }
                if (command.Version == Protocol.Version && (command.Name is "shutdown" or "clearProfile" || command.Name == "manual" && command.Enabled || command.Name == "reconnect" && !command.Enabled))
                    try { active?.Cancel(); } catch (ObjectDisposedException) { }
                await gate.WaitAsync(timeout.Token);
                Reply reply;
                try
                {
                    if (replies.TryGetValue(command.Id, out var cached)) reply = cached;
                    else
                    {
                        using var operation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                        active = operation;
                        reply = await ExecuteAsync(command, operation.Token);
                        if (command.Name != "status") { if (replies.Count >= 128) replies.Remove(replies.Keys.First()); replies[command.Id] = reply; }
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException) { reply = new(false, "操作未完成，请检查数据目录权限、配置与可用空间。", State); log.Write("错误", "后台", "command.failed", "配置或存储操作失败，原有配置保持可用。"); }
                catch (OperationCanceledException) { reply = new(false, "操作已取消。", State); }
                catch (System.Runtime.InteropServices.COMException) { reply = new(false, "无法读取 Windows 后台任务状态，请检查任务计划程序。", State); }
                finally { active = null; PublishState(); gate.Release(); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply, Protocol.Json));
                if (command.Name == "shutdown" && reply.Ok) lifetime.Cancel();
                else if ((reply.Ok && command.Name is "settings" or "reconnect" or "saveProfile" or "clearProfile") || (command.Name == "manual" && !command.Enabled)) Wake();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or InvalidDataException) { }
        }
    }
    private async Task<Reply> ExecuteAsync(Command c, CancellationToken ct)
    {
        if (c.Version != Protocol.Version) return new(false, "后台版本不匹配，请重新启动软件。", State);
        switch (c.Name)
        {
            case "status": return new(true, "", State);
            case "detect": await DetectAsync(ct); return new(true, lastNetwork.Detail, State);
            case "manual": manualUntil = c.Enabled ? DateTimeOffset.UtcNow.AddSeconds(30) : DateTimeOffset.MinValue; return new(true, "", State);
            case "validate":
                var profile = c.Profile ?? storage.Profile;
                if (profile is null) return new(false, "请先在认证网页获取登录信息。", State);
                profile.Validate();
                var auth = await network.LoginAsync(profile, ct);
                await DetectAsync(ct);
                log.Write(auth.State == "rejected" ? "警告" : "信息", "认证", "login.validate", auth.Message);
                return new(auth.Success || auth.State == "alreadyOnline", auth.Message, State, auth);
            case "saveProfile":
                if (c.Profile is not { Confirmed: true } p) return new(false, "请先完成一次成功的网页认证或验证登录。", State);
                if (legacyTaskRunning()) return new(false, "旧版守护仍在运行，请先在旧版暂停守护，避免重复登录。", State);
                storage.Save(storage.Settings with { AutoReconnect = true }, p); rejected = false; failures = 0; retryAt = DateTimeOffset.MinValue;
                guardian = "运行中"; log.Write("信息", "配置", "profile.saved", "登录信息已加密保存，自动重连已启用。"); return new(true, "登录信息已保存，后台守护已启用。", State);
            case "settings":
                if (c.Settings?.AutoReconnect == true && storage.Profile is not { Confirmed: true }) return new(false, "请先验证并保存登录信息，再开启自动重连。", State);
                storage.Save(c.Settings ?? throw new InvalidDataException(), storage.Profile);
                log.Write("信息", "配置", "settings.saved", "设置已保存并应用。"); return new(true, "设置已保存。", State);
            case "reconnect":
                if (c.Enabled && storage.Profile is not { Confirmed: true }) return new(false, "请先获取并保存经过验证的登录信息。", State);
                if (c.Enabled && legacyTaskRunning()) return new(false, "旧版守护仍在运行，请先暂停旧版。", State);
                storage.Save(storage.Settings with { AutoReconnect = c.Enabled }, storage.Profile);
                if (c.Enabled) { rejected = false; retryAt = DateTimeOffset.MinValue; }
                log.Write("信息", "守护", "guardian.toggle", c.Enabled ? "自动重连已启用。" : "自动重连已暂停。"); return new(true, c.Enabled ? "自动重连已启用。" : "已暂停自动重连，现有网络连接不受影响。", State);
            case "move": storage.MoveTo(c.DataDirectory ?? throw new InvalidDataException()); log.Write("信息", "配置", "storage.moved", "数据保存位置已更新，原目录保留备份。"); return new(true, "保存位置已更新，原目录保留备份。", State);
            case "shutdown": storage.Save(storage.Settings with { AutoReconnect = false }, storage.Profile); return new(true, "已暂停守护，后台将在处理完当前连接后退出。配置和日志已保留。", State);
            case "clearProfile": storage.Save(storage.Settings with { AutoReconnect = false }, null); rejected = false; log.Write("信息", "配置", "profile.removed", "已清除保存的登录信息并暂停重连。"); return new(true, "登录信息已清除，现有网络连接不受影响。", State);
            case "clearLogs": log.Clear(); log.Write("信息", "日志", "logs.cleared", "历史日志已清除。"); return new(true, "历史日志已清除，新的后台事件仍会继续记录。", State);
            default: return new(false, "不支持的操作。", State);
        }
    }
    private async Task DetectAsync(CancellationToken ct)
    {
        var previous = lastNetwork.State;
        lastNetwork = await network.DetectAsync(ct);
        PublishState();
        if (previous != lastNetwork.State) log.Write(lastNetwork.State == "online" ? "信息" : "警告", "网络", "network.changed", lastNetwork.Detail);
    }
    private async Task LoopAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await gate.WaitAsync(lifetime.Token);
                try
                {
                    using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    active = operation;
                    await DetectAsync(operation.Token);
                    authSamples = lastNetwork.State == "authentication" ? authSamples + 1 : 0;
                    if (storage.Profile is not null && storage.Settings.AutoReconnect)
                    {
                        guardian = "运行中";
                        if (lastNetwork.State == "online") { failures = 0; retryAt = DateTimeOffset.MinValue; }
                        else if (authSamples >= 2 && !rejected && DateTimeOffset.UtcNow >= manualUntil && DateTimeOffset.UtcNow >= retryAt)
                        {
                            guardian = "重连中"; PublishState(); log.Write("信息", "守护", "reconnect.begin", "正在尝试恢复校园网连接。");
                            var result = await network.LoginAsync(storage.Profile, operation.Token);
                            log.Write(result.State == "rejected" ? "警告" : "信息", "守护", "reconnect.result", result.Message);
                            if (result.State == "rejected") rejected = true;
                            await Task.Delay(2000, operation.Token); await DetectAsync(operation.Token);
                            failures = lastNetwork.State == "online" ? 0 : Math.Min(failures + 1, 6);
                            retryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, storage.Settings.IntervalSeconds * Math.Pow(2, failures)));
                            guardian = lastNetwork.State == "online" ? "运行中" : "等待网络恢复";
                        }
                    }
                    if (!storage.Settings.AutoReconnect && DateTimeOffset.UtcNow - new DateTimeOffset(Interlocked.Read(ref lastClientTicks), TimeSpan.Zero) > TimeSpan.FromSeconds(45)) lifetime.Cancel();
                }
                finally { active = null; PublishState(); gate.Release(); }
                await wake.WaitAsync(TimeSpan.FromSeconds(storage.Settings.AutoReconnect ? storage.Settings.IntervalSeconds : Math.Min(15, storage.Settings.IntervalSeconds)), lifetime.Token);
            }
            catch (OperationCanceledException) { if (lifetime.IsCancellationRequested) break; }
            catch { log.Write("错误", "后台", "worker.error", "后台操作发生异常，将在下一次检查重试。"); try { await Task.Delay(5000, lifetime.Token); } catch (OperationCanceledException) { break; } }
        }
    }
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); network.Dispose(); gate.Dispose(); wake.Dispose(); }
}
