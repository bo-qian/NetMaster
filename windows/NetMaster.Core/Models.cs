using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Principal;

namespace NetMaster.Core;

public static class Protocol
{
    public const int Version = 1;
    public const string Portal = "http://10.10.9.9";
    public const string Login = Portal + "/eportal/InterFace.do?method=login";
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static string UserKey => WindowsIdentity.GetCurrent().User!.Value.Replace('-', '_');
    public static string PipeName(string home) => "NetMaster_v1_" + UserKey + "_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(home).ToUpperInvariant())))[..12];
    public static bool IsLogin(string uri, string method) => method.Equals("POST", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == "http" && u.Host == "10.10.9.9" && u.Port == 80 && u.AbsolutePath.Equals("/eportal/InterFace.do", StringComparison.OrdinalIgnoreCase) && Form(u.Query.TrimStart('?')).GetValueOrDefault("method") == "login";
    public static Dictionary<string, string> Form(string data)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data.Split('&'))
        {
            var p = pair.Split('=', 2);
            fields[Uri.UnescapeDataString(p[0].Replace('+', ' '))] = p.Length == 2 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "";
        }
        return fields;
    }
    public static string Mask(string value) => string.IsNullOrEmpty(value) ? "未获取" : value.Length <= 4 ? "****" : value[..2] + "****" + value[^2..];
    public static async Task<string?> ReadMessageAsync(TextReader reader, CancellationToken ct, int limit = 100000)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[1024];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) return text.Length == 0 ? null : text.ToString();
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            var take = newline < 0 ? count : newline;
            if (text.Length + take > limit) throw new InvalidDataException("后台消息超过允许长度。");
            text.Append(buffer, 0, take);
            if (newline >= 0) return text.ToString().TrimEnd('\r');
        }
    }
}

public sealed record Settings
{
    public int IntervalSeconds { get; init; } = 20;
    public int RetentionDays { get; init; } = 7;
    public int Theme { get; init; }
    public bool AutoReconnect { get; init; }
    public void Validate()
    {
        if (IntervalSeconds is < 5 or > 3600 || RetentionDays is < 1 or > 365 || Theme is < 0 or > 2) throw new InvalidDataException("检测间隔应为 5–3600 秒，日志保留应为 1–365 天。");
    }
}
public sealed record LoginProfile
{
    public string Payload { get; init; } = "";
    public bool Confirmed { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }
    [JsonIgnore] public string Account => Protocol.Form(Payload ?? "").GetValueOrDefault("userId", "");
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Payload) || Payload.Length > 65536) throw new InvalidDataException("未获取到完整的校园网认证信息，请在内嵌网页重新登录。");
        var f = Protocol.Form(Payload);
        if (string.IsNullOrWhiteSpace(Account) || !f.Any(field => !string.IsNullOrWhiteSpace(field.Value) && (field.Key.Equals("password", StringComparison.OrdinalIgnoreCase) || field.Key.Equals("pwd", StringComparison.OrdinalIgnoreCase) || field.Key.Equals("mm", StringComparison.OrdinalIgnoreCase)))) throw new InvalidDataException("未获取到完整的校园网认证信息，请在内嵌网页重新登录。");
    }
    public override string ToString() => "校园网认证信息（已隐藏）";
}
public sealed record NetworkResult(string State, DateTimeOffset CheckedAt, string Detail);
public sealed record AuthResult(string State, string Message)
{
    public bool Success => State == "success";
}
public sealed record Snapshot
{
    public int Version { get; init; } = Protocol.Version;
    public DateTimeOffset Heartbeat { get; init; } = DateTimeOffset.UtcNow;
    public NetworkResult Network { get; init; } = new("unknown", DateTimeOffset.UtcNow, "尚未检测");
    public string Guardian { get; init; } = "未配置";
    public bool HasProfile { get; init; }
    public string Account { get; init; } = "未获取";
    public Settings Settings { get; init; } = new();
    public string DataDirectory { get; init; } = "";
    public string? StorageError { get; init; }
}
public sealed record Command
{
    public int Version { get; init; } = Protocol.Version;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "status";
    public Settings? Settings { get; init; }
    public LoginProfile? Profile { get; init; }
    public string? DataDirectory { get; init; }
    public bool Enabled { get; init; }
}
public sealed record Reply(bool Ok, string Message, Snapshot? Snapshot = null, AuthResult? Authentication = null);
public sealed record LogEntry(string Id, DateTimeOffset Time, string Level, string Source, string Event, string Message)
{
    [JsonIgnore] public string TimeLabel => Time.ToLocalTime().ToString("MM-dd HH:mm:ss");
    [JsonIgnore] public string Display => $"{Time.ToLocalTime():MM-dd HH:mm:ss}   {Level}   {Message}";
    [JsonIgnore] public string Details => $"时间：{Time.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n级别：{Level}\n来源：{Source}\n事件：{Event}\n记录 ID：{Id}\n\n{Message}";
}
