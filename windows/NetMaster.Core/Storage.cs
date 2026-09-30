using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetMaster.Core;

public sealed class AppStorage
{
    private sealed record Envelope(int Schema, Settings Settings, string? Credential);
    public string Home { get; }
    public string DataDirectory { get; private set; }
    public Settings Settings { get; private set; } = new();
    public LoginProfile? Profile { get; private set; }
    public string? Error { get; private set; }
    public static string DefaultHome => Environment.GetEnvironmentVariable("NETMASTER_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetMaster", "WinUI");
    public AppStorage(string? home = null)
    {
        Home = Path.GetFullPath(home ?? DefaultHome);
        DataDirectory = Path.Combine(Home, "data");
        try
        {
            if (File.Exists(Path.Combine(Home, "location.json"))) DataDirectory = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(Home, "location.json"))) ?? DataDirectory;
            DataDirectory = Path.GetFullPath(DataDirectory);
            var config = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(config))
            {
                var e = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(config), Protocol.Json) ?? throw new InvalidDataException();
                if (e.Schema != 1) throw new InvalidDataException();
                e.Settings.Validate();
                Settings = e.Settings;
                if (e.Credential is not null)
                {
                    var clear = ProtectedData.Unprotect(Convert.FromBase64String(e.Credential), null, DataProtectionScope.CurrentUser);
                    try { Profile = JsonSerializer.Deserialize<LoginProfile>(clear, Protocol.Json); Profile?.Validate(); }
                    finally { CryptographicOperations.ZeroMemory(clear); }
                }
            }
        }
        catch { Profile = null; Settings = new(); Error = "配置损坏或当前 Windows 用户无法读取凭证。原文件已保留，请重新获取登录信息后保存。"; }
    }
    public void Save(Settings settings, LoginProfile? profile)
    {
        settings.Validate(); profile?.Validate();
        string? secret = null;
        if (profile is not null)
        {
            var clear = JsonSerializer.SerializeToUtf8Bytes(profile, Protocol.Json);
            try { secret = Convert.ToBase64String(ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        var config = Path.Combine(DataDirectory, "settings.json");
        if (Error is not null && File.Exists(config))
            File.Copy(config, config + ".recovery-" + Guid.NewGuid().ToString("N"), false);
        AtomicWrite(config, JsonSerializer.Serialize(new Envelope(1, settings, secret), Protocol.Json));
        Settings = settings; Profile = profile; Error = null;
    }
    public void MoveTo(string parent)
    {
        // Own one child directory; never move/delete the user's selected parent.
        var destination = Path.GetFullPath(Path.Combine(parent, "NetMaster-WinUI"));
        if (destination.Equals(DataDirectory, StringComparison.OrdinalIgnoreCase)) return;
        if (destination.StartsWith(DataDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("不能把数据目录移入自身。");
        if (DataDirectory.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.Equals(Home, StringComparison.OrdinalIgnoreCase)) throw new IOException("不能使用包含当前应用数据的目录。");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("目标 NetMaster-WinUI 目录已有数据，请选择其他位置。");
        Directory.CreateDirectory(parent);
        var staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
        foreach (var name in new[] { "settings.json" })
            if (File.Exists(Path.Combine(DataDirectory, name))) File.Copy(Path.Combine(DataDirectory, name), Path.Combine(staging, name), false);
        var logs = Path.Combine(DataDirectory, "logs");
        if (Directory.Exists(logs))
        {
            Directory.CreateDirectory(Path.Combine(staging, "logs"));
            foreach (var file in Directory.EnumerateFiles(logs, "????-??-??.jsonl")) File.Copy(file, Path.Combine(staging, "logs", Path.GetFileName(file)), false);
        }
        if (Directory.Exists(destination)) Directory.Delete(destination, false);
        Directory.Move(staging, destination);
        AtomicWrite(Path.Combine(Home, "location.json"), JsonSerializer.Serialize(destination));
        DataDirectory = destination;
        }
        finally
        {
            // Only the unique staging directory created by this operation is removed.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        // Preserve the old copy as a recovery backup; cleanup is an explicit operation.
    }
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(text); writer.Flush(); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class LogStore(AppStorage storage)
{
    private readonly object gate = new();
    public string? LastError { get; private set; }
    // Messages are app-owned templates, never raw server responses or exceptions.
    public void Write(string level, string source, string code, string message)
    {
        lock (gate)
        {
            try
            {
                var folder = Path.Combine(storage.DataDirectory, "logs"); Directory.CreateDirectory(folder);
                var entry = new LogEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, level, source, code, message);
                using (var stream = new FileStream(Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(JsonSerializer.Serialize(entry, Protocol.Json));
                foreach (var file in Directory.EnumerateFiles(folder, "????-??-??.jsonl"))
                    if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", out var date) && date < DateOnly.FromDateTime(DateTime.Today.AddDays(1 - storage.Settings.RetentionDays))) File.Delete(file);
                LastError = null;
            }
            catch { LastError = "日志保存失败，请检查保存位置和剩余空间。"; }
        }
    }
    public static IReadOnlyList<LogEntry> Read(string directory)
    {
        var list = new List<LogEntry>(); var folder = Path.Combine(directory, "logs");
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "????-??-??.jsonl").OrderDescending())
        {
            try { list.AddRange(ReadFile(file)); }
            catch (FileNotFoundException) { }
        }
        return list.DistinctBy(e => e.Id).OrderByDescending(e => e.Time).ToList();
    }
    internal static IReadOnlyList<LogEntry> ReadFile(string file)
    {
        var entries = new List<LogEntry>();
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            try { if (JsonSerializer.Deserialize<LogEntry>(line, Protocol.Json) is { Id: not null, Level: not null, Source: not null, Event: not null, Message: not null } e) entries.Add(e); } catch (JsonException) { }
        return entries;
    }
    public void Clear()
    {
        lock (gate)
        {
            var folder = Path.Combine(storage.DataDirectory, "logs");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "????-??-??.jsonl"))
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", out _)) File.Delete(file);
        }
    }
}

// Cache immutable daily files; reread only files whose size or timestamp changed.
public sealed class LogReader
{
    private sealed record Cached(long Length, DateTime Modified, IReadOnlyList<LogEntry> Entries);
    private readonly Dictionary<string, Cached> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    public IReadOnlyList<LogEntry> Read(string directory)
    {
        lock (gate) return ReadLocked(directory);
    }
    private IReadOnlyList<LogEntry> ReadLocked(string directory)
    {
        var folder = Path.Combine(directory, "logs");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "????-??-??.jsonl") : Array.Empty<string>();
        var current = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in cache.Keys.Where(key => !current.Contains(key)).ToArray()) cache.Remove(key);
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (!cache.TryGetValue(file, out var old) || old.Length != info.Length || old.Modified != info.LastWriteTimeUtc)
                    cache[file] = new(info.Length, info.LastWriteTimeUtc, LogStore.ReadFile(file));
            }
            catch (FileNotFoundException) { cache.Remove(file); }
        }
        return cache.Values.SelectMany(value => value.Entries).DistinctBy(entry => entry.Id).OrderByDescending(entry => entry.Time).ToList();
    }
}
