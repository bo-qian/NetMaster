using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

internal sealed class FakeNetwork : INetworkService
{
    public string State { get; set; } = "online";
    public string Authentication { get; set; } = "success";
    public int DetectCount;
    public int LoginCount;
    public TaskCompletionSource? BlockDetection;
    public TaskCompletionSource? BlockLogin;
    public TaskCompletionSource LoginStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<NetworkResult> DetectAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref DetectCount);
        if (BlockDetection is { } block) await block.Task.WaitAsync(ct);
        return new(State, DateTimeOffset.UtcNow, "可控测试网络状态");
    }
    public async Task<AuthResult> LoginAsync(LoginProfile profile, CancellationToken ct)
    {
        Interlocked.Increment(ref LoginCount); LoginStarted.TrySetResult();
        if (BlockLogin is { } block) await block.Task.WaitAsync(ct);
        return new(Authentication, "可控测试认证结果");
    }
    public void Dispose() { }
}

internal sealed class EngineHost : IAsyncDisposable
{
    private readonly TestDirectory folder = new();
    private readonly GuardianEngine engine;
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
    private readonly Task run;
    public AppStorage Storage { get; }
    public FakeNetwork Network { get; }
    public EngineHost(FakeNetwork? network = null, bool legacyRunning = false, Func<bool>? legacyCheck = null)
    {
        Storage = new(folder.PathName); Network = network ?? new();
        engine = new(Storage, Network, legacyCheck ?? (() => legacyRunning), false); run = engine.RunAsync(lifetime.Token);
    }
    public async Task<Reply> Send(Command command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", Protocol.PipeName(Storage.Home), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(command, Protocol.Json));
        return JsonSerializer.Deserialize<Reply>((await Protocol.ReadMessageAsync(reader, timeout.Token))!, Protocol.Json)!;
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); engine.Dispose(); lifetime.Dispose(); folder.Dispose();
    }
}

public sealed class GuardianTests
{
    [Fact]
    public async Task BeginningConfigurationDeletesSavedCredentialsAndCancelDoesNotRestore()
    {
        await using var host = new EngineHost();
        Assert.True((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        var started = await host.Send(new() { Name = "beginConfiguration" });
        Assert.True(started.Ok); Assert.False(started.Snapshot!.HasProfile); Assert.False(started.Snapshot.Settings.AutoReconnect);
        Assert.Null(new AppStorage(host.Storage.Home).Profile);
        Assert.False((await host.Send(new() { Name = "validateCandidate" })).Ok);
        Assert.Equal(0, host.Network.LoginCount);
        var cancelled = await host.Send(new() { Name = "cancelConfiguration" });
        Assert.True(cancelled.Ok); Assert.False(cancelled.Snapshot!.HasProfile); Assert.False(cancelled.Snapshot.Settings.AutoReconnect);
        Assert.Null(new AppStorage(host.Storage.Home).Profile);
        Assert.DoesNotContain(Directory.EnumerateFiles(host.Storage.DataDirectory), name => name.Contains("recovery") || name.EndsWith(".bak"));
    }

    [Fact]
    public async Task AlreadyOnlineValidationKeepsSavedProfileAndDoesNotConfirmReplacement()
    {
        await using var host = new EngineHost(new() { Authentication = "alreadyOnline" });
        Assert.True((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        var replacement = StorageTests.FakeProfile with { Payload = "userId=fixture-new&password=fixture-new-secret", Confirmed = false };
        var result = await host.Send(new() { Name = "validate", Profile = replacement });
        Assert.True(result.Ok);
        Assert.Equal("alreadyOnline", result.Authentication!.State);
        Assert.False(result.Authentication.Success);
        Assert.Equal(StorageTests.FakeProfile, host.Storage.Profile);
        Assert.False((await host.Send(new() { Name = "saveProfile", Profile = replacement })).Ok);
        Assert.Equal(StorageTests.FakeProfile, new AppStorage(host.Storage.Home).Profile);
    }

    [Fact]
    public async Task SaveEncryptsAndEnablesGuardWhenWindowsTaskIsAbsent()
    {
        var missingTask = "NetMaster_Missing_Test_" + Guid.NewGuid().ToString("N");
        await using var host = new EngineHost(legacyCheck: () => StartupRegistration.FindTask(missingTask) is not null);
        var saved = await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile });
        Assert.True(saved.Ok, saved.Message);
        Assert.True(saved.Snapshot!.HasProfile);
        Assert.True(saved.Snapshot.Settings.AutoReconnect);
        var restored = new AppStorage(host.Storage.Home);
        Assert.Equal(StorageTests.FakeProfile, restored.Profile);
        Assert.True(restored.Settings.AutoReconnect);
        Assert.Equal(0, host.Network.LoginCount);
    }
    [Fact]
    public async Task MalformedCredentialsAreRejectedWithoutTerminatingCommandServer()
    {
        await using var host = new EngineHost();
        Assert.False((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile with { Payload = null! } })).Ok);
        Assert.True((await host.Send(new())).Ok);
        Assert.Equal(0, host.Network.LoginCount);
    }
    [Fact]
    public async Task RejectedCredentialsStopFurtherAutomaticAttempts()
    {
        await using var host = new EngineHost(new() { State = "authentication", Authentication = "rejected" });
        Assert.True((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        await host.Network.LoginStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.Send(new() { Name = "settings", Settings = host.Storage.Settings with { Theme = 2 } });
        Assert.Equal(1, host.Network.LoginCount);
        Assert.Equal("等待重新登录", (await host.Send(new())).Snapshot!.Guardian);
    }

    [Fact]
    public async Task ClearCredentialsPausesAndClearsPersistedProfile()
    {
        await using var host = new EngineHost();
        Assert.True((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        var cleared = await host.Send(new() { Name = "clearProfile" });
        Assert.True(cleared.Ok); Assert.False(cleared.Snapshot!.HasProfile);
        var restored = new AppStorage(host.Storage.Home);
        Assert.Null(restored.Profile); Assert.False(restored.Settings.AutoReconnect);
    }

    [Fact]
    public async Task UnconfiguredGuardianRejectsEnablingAndUnconfirmedCredentials()
    {
        await using var host = new EngineHost();
        Assert.False((await host.Send(new() { Name = "reconnect", Enabled = true })).Ok);
        Assert.False((await host.Send(new() { Name = "settings", Settings = new() { AutoReconnect = true } })).Ok);
        Assert.False((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile with { Confirmed = false } })).Ok);
        var state = (await host.Send(new())).Snapshot!;
        Assert.False(state.HasProfile); Assert.False(state.Settings.AutoReconnect); Assert.Equal(0, host.Network.LoginCount);
    }

    [Fact]
    public async Task OldTaskConflictPreventsNewGuardian()
    {
        await using var host = new EngineHost(legacyRunning: true);
        Assert.False((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        Assert.Null(host.Storage.Profile);
    }

    [Fact]
    public async Task ConcurrentStatusRemainsAvailableDuringNetworkOperation()
    {
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new EngineHost(new() { BlockDetection = block });
        var queries = Enumerable.Range(0, 8).Select(_ => host.Send(new())).ToArray();
        var replies = await Task.WhenAll(queries).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(replies, reply => Assert.True(reply.Ok)); Assert.All(replies, reply => Assert.Equal("unknown", reply.Snapshot!.Network.State));
    }

    [Fact]
    public async Task PauseCancelsPendingValidationAndPersistsAcrossReopen()
    {
        await using var host = new EngineHost(new() { BlockLogin = new(TaskCreationOptions.RunContinuationsAsynchronously) });
        Assert.True((await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile })).Ok);
        var validation = host.Send(new() { Name = "validate" });
        await host.Network.LoginStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var paused = await host.Send(new() { Name = "reconnect", Enabled = false });
        Assert.True(paused.Ok); Assert.False((await validation).Ok);
        Assert.False(new AppStorage(host.Storage.Home).Settings.AutoReconnect);
    }

    [Fact]
    public async Task SettingsCommandsAreDeduplicatedAndWakeLongIntervalLoop()
    {
        await using var host = new EngineHost();
        var settings = new Command { Name = "settings", Settings = new() { IntervalSeconds = 3600, Theme = 2 } };
        Assert.True((await host.Send(settings)).Ok); Assert.True((await host.Send(settings)).Ok);
        Assert.Single(LogStore.Read(host.Storage.DataDirectory), e => e.Event == "settings.saved");
        var count = Volatile.Read(ref host.Network.DetectCount);
        Assert.True((await host.Send(new() { Name = "settings", Settings = new() { IntervalSeconds = 5 } })).Ok);
        var until = DateTime.UtcNow.AddSeconds(3);
        while (Volatile.Read(ref host.Network.DetectCount) <= count && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.True(Volatile.Read(ref host.Network.DetectCount) > count);
    }

    [Fact]
    public async Task SavingProfilePreservesManualLeaseAndSnapshotsContainNoCredentials()
    {
        await using var host = new EngineHost(new() { State = "authentication" });
        Assert.True((await host.Send(new() { Name = "manual", Enabled = true })).Ok);
        var saved = await host.Send(new() { Name = "saveProfile", Profile = StorageTests.FakeProfile });
        Assert.True(saved.Ok); Assert.Contains("暂缓", saved.Snapshot!.Guardian);
        var status = await host.Send(new());
        var text = JsonSerializer.Serialize(status, Protocol.Json);
        Assert.DoesNotContain("fixture-secret", text); Assert.DoesNotContain("fixture-user", text); Assert.Equal(0, host.Network.LoginCount);
    }

    [Fact]
    public async Task ProtocolMismatchCannotChangePersistedSettings()
    {
        await using var host = new EngineHost();
        var response = await host.Send(new() { Version = 99, Name = "settings", Settings = new() { Theme = 2 } });
        Assert.False(response.Ok); Assert.Equal(0, host.Storage.Settings.Theme);
    }
}
