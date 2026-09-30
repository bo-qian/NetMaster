using System.Text.Json;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

internal sealed class TestDirectory : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "NetMasterTests");
    public string PathName { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(PathName);
    public void Dispose()
    {
        var absolute = Path.GetFullPath(PathName);
        if (!absolute.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
}

public sealed class StorageTests
{
    internal static LoginProfile FakeProfile => new() { Payload = "userId=fixture-user&password=fixture-secret&service=fixture", Confirmed = true };

    [Fact]
    public void LogReaderHandlesAppendsTruncationAndClearWithoutDeletingUnrelatedFiles()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(folder.PathName);
        var writer = new LogStore(storage); var reader = new LogReader();
        writer.Write("信息", "测试", "one", "测试记录一"); Assert.Single(reader.Read(storage.DataDirectory));
        writer.Write("信息", "测试", "two", "测试记录二"); Assert.Equal(2, reader.Read(storage.DataDirectory).Count);
        var logs = Path.Combine(storage.DataDirectory, "logs");
        var unrelated = Path.Combine(logs, "keep.txt"); File.WriteAllText(unrelated, "keep");
        File.WriteAllText(Assert.Single(Directory.EnumerateFiles(logs, "*.jsonl")), "");
        Assert.Empty(reader.Read(storage.DataDirectory));
        writer.Write("信息", "测试", "three", "测试记录三"); Assert.Single(reader.Read(storage.DataDirectory));
        writer.Clear(); Assert.Empty(reader.Read(storage.DataDirectory)); Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void CredentialRoundTripsWithoutPlaintextOnDiskOrInSnapshot()
    {
        using var folder = new TestDirectory();
        var storage = new AppStorage(folder.PathName);
        var settings = new Settings { AutoReconnect = true, Theme = 2, IntervalSeconds = 5 };
        storage.Save(settings, FakeProfile);
        var raw = File.ReadAllText(Path.Combine(storage.DataDirectory, "settings.json"));
        Assert.DoesNotContain("fixture-secret", raw);
        Assert.DoesNotContain("fixture-user", raw);
        var restored = new AppStorage(folder.PathName);
        Assert.Null(restored.Error);
        Assert.Equal(FakeProfile, restored.Profile);
        Assert.Equal(settings, restored.Settings);
        using var engine = new GuardianEngine(restored, new FakeNetwork(), () => false, false);
        Assert.DoesNotContain("fixture-secret", JsonSerializer.Serialize(new Snapshot { Account = Protocol.Mask(restored.Profile!.Account) }, Protocol.Json));
        Assert.DoesNotContain("fixture-secret", FakeProfile.ToString());
    }

    [Fact]
    public void InvalidSavePreservesPreviousFileAndState()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(folder.PathName);
        storage.Save(new(), FakeProfile);
        var path = Path.Combine(storage.DataDirectory, "settings.json"); var original = File.ReadAllText(path);
        Assert.Throws<InvalidDataException>(() => storage.Save(new Settings { IntervalSeconds = 0 }, null));
        Assert.Equal(original, File.ReadAllText(path)); Assert.Equal(FakeProfile, storage.Profile);
        Assert.Empty(Directory.EnumerateFiles(storage.DataDirectory, "*.tmp"));
    }

    [Fact]
    public void DamagedConfigurationDefaultsToPausedAndRecoveryCopySurvivesSave()
    {
        using var folder = new TestDirectory(); var original = new AppStorage(folder.PathName);
        var path = Path.Combine(original.DataDirectory, "settings.json"); AppStorage.AtomicWrite(path, "{ damaged fixture }");
        var restored = new AppStorage(folder.PathName);
        Assert.NotNull(restored.Error); Assert.Null(restored.Profile); Assert.False(restored.Settings.AutoReconnect);
        restored.Save(new(), null);
        Assert.Equal("{ damaged fixture }", File.ReadAllText(Assert.Single(Directory.EnumerateFiles(restored.DataDirectory, "settings.json.recovery-*"))));
        Assert.Null(restored.Error);
    }

    [Fact]
    public void MigrationCopiesOwnedDataAndRetainsOriginal()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(Path.Combine(folder.PathName, "home"));
        storage.Save(new(), FakeProfile); new LogStore(storage).Write("信息", "测试", "fixture", "测试记录");
        var original = storage.DataDirectory; File.WriteAllText(Path.Combine(original, "unrelated.txt"), "keep");
        storage.MoveTo(Path.Combine(folder.PathName, "destination"));
        var restored = new AppStorage(storage.Home);
        Assert.Equal(storage.DataDirectory, restored.DataDirectory); Assert.Equal(FakeProfile, restored.Profile);
        Assert.Single(LogStore.Read(restored.DataDirectory)); Assert.True(File.Exists(Path.Combine(original, "settings.json")));
        Assert.False(File.Exists(Path.Combine(restored.DataDirectory, "unrelated.txt")));
    }

    [Fact]
    public void MigrationRejectsNonemptyAndNestedTargetsWithoutChangingLocation()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(Path.Combine(folder.PathName, "home"));
        storage.Save(new(), FakeProfile); var original = storage.DataDirectory;
        var parent = Path.Combine(folder.PathName, "occupied"); var destination = Path.Combine(parent, "NetMaster-WinUI");
        Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");
        Assert.Throws<IOException>(() => storage.MoveTo(parent));
        Assert.Throws<IOException>(() => storage.MoveTo(original));
        Assert.Equal(original, storage.DataDirectory); Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(storage.Home, "location.json")));
    }

    [Fact]
    public void FailedMigrationCopyCleansStagingAndCanBeRetried()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(Path.Combine(folder.PathName, "home"));
        storage.Save(new(), FakeProfile); var original = storage.DataDirectory; var parent = Path.Combine(folder.PathName, "target");
        using (var held = new FileStream(Path.Combine(original, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => storage.MoveTo(parent));
        Assert.Equal(original, storage.DataDirectory); Assert.Empty(Directory.EnumerateDirectories(parent));
        storage.MoveTo(parent); Assert.Equal(FakeProfile, new AppStorage(storage.Home).Profile);
    }

    [Fact]
    public void LogRetentionRemovesOnlyExpiredDailyFilesAndToleratesPartialLines()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(folder.PathName); storage.Save(new Settings { RetentionDays = 1 }, null);
        var logs = Path.Combine(storage.DataDirectory, "logs"); Directory.CreateDirectory(logs);
        var old = Path.Combine(logs, $"{DateTime.Today.AddDays(-1):yyyy-MM-dd}.jsonl"); File.WriteAllText(old, "fixture");
        var keep = Path.Combine(logs, "unrelated.txt"); File.WriteAllText(keep, "keep");
        var store = new LogStore(storage); store.Write("信息", "测试", "fixture", "测试记录");
        Assert.Null(store.LastError); Assert.False(File.Exists(old)); Assert.True(File.Exists(keep));
        File.AppendAllText(Path.Combine(logs, $"{DateTime.Today:yyyy-MM-dd}.jsonl"), "{partial");
        Assert.Single(LogStore.Read(storage.DataDirectory));
    }
}
