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
    [Fact]
    public void ExplicitResetDoesNotBackUpDamagedCredentials()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(folder.PathName);
        AppStorage.AtomicWrite(Path.Combine(storage.DataDirectory, "settings.json"), "{ damaged fixture }");
        storage = new AppStorage(folder.PathName);
        storage.Save(new(), null, preserveRecovery: false);
        Assert.Null(storage.Profile); Assert.Null(storage.Error);
        Assert.Empty(Directory.EnumerateFiles(storage.DataDirectory, "*.recovery-*"));
    }

    [Fact]
    public void SettingsAndLocationAreCommittedTogetherAndInvalidTargetPreservesBoth()
    {
        using var folder = new TestDirectory(); var storage = new AppStorage(Path.Combine(folder.PathName, "home"));
        storage.Save(new(), FakeProfile);
        var original = storage.DataDirectory; var occupied = Path.Combine(folder.PathName, "occupied");
        Directory.CreateDirectory(Path.Combine(occupied, "NetMaster"));
        File.WriteAllText(Path.Combine(occupied, "NetMaster", "keep.txt"), "fixture");
        var updated = new Settings { IntervalSeconds = 30, Theme = 2 };
        Assert.Throws<IOException>(() => storage.MoveTo(occupied, updated));
        Assert.Equal(original, storage.DataDirectory); Assert.Equal(new Settings(), new AppStorage(storage.Home).Settings);
        storage.MoveTo(Path.Combine(folder.PathName, "target"), updated);
        var restored = new AppStorage(storage.Home);
        Assert.Equal(updated, restored.Settings); Assert.Equal(storage.DataDirectory, restored.DataDirectory); Assert.Equal(FakeProfile, restored.Profile);
    }

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
        var parent = Path.Combine(folder.PathName, "occupied"); var destination = Path.Combine(parent, "NetMaster");
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
    public void PreviousWindowsDefaultDataIsCopiedIntoNewHome()
    {
        using var folder = new TestDirectory();
        var home = Path.Combine(folder.PathName, "NetMaster");
        var previous = new AppStorage(Path.Combine(home, "WinUI"));
        previous.Save(new Settings { AutoReconnect = true }, FakeProfile);
        new LogStore(previous).Write("信息", "测试", "migration", "旧版记录");

        var current = new AppStorage(home, previous.Home);
        Assert.Equal(Path.Combine(home, "data"), current.DataDirectory);
        Assert.Equal(FakeProfile, current.Profile);
        Assert.True(current.Settings.AutoReconnect);
        Assert.Single(LogStore.Read(current.DataDirectory));
        Assert.True(File.Exists(Path.Combine(previous.DataDirectory, "settings.json")));
    }

    [Fact]
    public void PreviousWindowsCustomDataGetsRenamedWithoutOverwritingExistingData()
    {
        using var folder = new TestDirectory();
        var home = Path.Combine(folder.PathName, "NetMaster");
        var previous = new AppStorage(Path.Combine(home, "WinUI"));
        previous.Save(new(), FakeProfile);
        var customParent = Path.Combine(folder.PathName, "custom");
        var oldData = Path.Combine(customParent, "NetMaster-WinUI");
        Directory.CreateDirectory(oldData);
        File.Copy(Path.Combine(previous.DataDirectory, "settings.json"), Path.Combine(oldData, "settings.json"));
        AppStorage.AtomicWrite(Path.Combine(previous.Home, "location.json"), JsonSerializer.Serialize(oldData));
        var current = new AppStorage(home, previous.Home);
        Assert.Equal(Path.Combine(customParent, "NetMaster"), current.DataDirectory);
        Assert.Equal(FakeProfile, current.Profile);
        Assert.True(File.Exists(Path.Combine(oldData, "settings.json")));

        var replacement = new LoginProfile { Payload = "userId=another&password=fixture", Confirmed = true };
        current.Save(new(), replacement);
        Assert.Equal(replacement, new AppStorage(home, previous.Home).Profile);
    }

    [Fact]
    public void OccupiedNewDataDirectoryKeepsPreviousCredentialsAvailable()
    {
        using var folder = new TestDirectory();
        var home = Path.Combine(folder.PathName, "NetMaster");
        var previous = new AppStorage(Path.Combine(home, "WinUI"));
        previous.Save(new(), FakeProfile);
        var destination = Path.Combine(home, "data");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "unrelated");

        var current = new AppStorage(home, previous.Home);
        Assert.Equal(previous.DataDirectory, current.DataDirectory);
        Assert.Equal(FakeProfile, current.Profile);
        Assert.NotNull(current.Error);
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(destination, "keep.txt")));
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
