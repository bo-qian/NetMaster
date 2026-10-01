using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace NetMaster.Core;

public interface INetworkService : IDisposable
{
    Task<NetworkResult> DetectAsync(CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginProfile profile, CancellationToken ct);
}

public sealed class NetworkService : INetworkService
{
    private readonly HttpClient http;
    private readonly Func<bool> networkAvailable;
    public NetworkService(HttpMessageHandler? handler = null, Func<bool>? networkAvailable = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        this.networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
    }
    public async Task<NetworkResult> DetectAsync(CancellationToken ct)
    {
        if (!networkAvailable()) return Result("offline", "没有可用的网络连接");
        bool portalRedirect = false;
        foreach (var probe in new[] { ("http://www.msftconnecttest.com/connecttest.txt", "Microsoft Connect Test"), ("http://connect.rom.miui.com/generate_204", ""), ("https://cp.cloudflare.com/generate_204", "") })
        {
            try
            {
                using var response = await http.GetAsync(probe.Item1, HttpCompletionOption.ResponseHeadersRead, ct);
                if (probe.Item2.Length == 0 && response.StatusCode == HttpStatusCode.NoContent || probe.Item2.Length > 0 && response.StatusCode == HttpStatusCode.OK && (await ReadBoundedAsync(response.Content, 4096, ct)).Trim() == probe.Item2) return Result("online", "互联网连接正常");
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location && new Uri(new Uri(probe.Item1), location).Host == "10.10.9.9") portalRedirect = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
        }
        if (portalRedirect) return Result("authentication", "需要校园网认证");
        try
        {
            using var response = await http.GetAsync(Protocol.Portal, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 400) return Result("authentication", "校园网登录页可打开，暂未确认能上网。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        return Result("uncertain", "检测未通过，可能是网络或检测服务暂时不可用");
    }
    private static NetworkResult Result(string state, string text) => new(state, DateTimeOffset.Now, text);
    public async Task<AuthResult> LoginAsync(LoginProfile profile, CancellationToken ct)
    {
        profile.Validate();
        using var request = new HttpRequestMessage(HttpMethod.Post, Protocol.Login);
        request.Content = new StringContent(profile.Payload, Encoding.UTF8, "application/x-www-form-urlencoded");
        // The school's interface returns an empty body without the browser
        // User-Agent used by windows/legacy/main.py, even for parameter errors.
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
        request.Content.Headers.ContentType!.CharSet = "UTF-8";
        request.Headers.TryAddWithoutValidation("Origin", Protocol.Portal);
        request.Headers.Referrer = new Uri(Protocol.Portal + "/eportal/index.jsp");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return new("unavailable", "校园网认证服务暂时不可用。");
            return ParseAuthentication(await ReadBoundedAsync(response.Content, 131072, ct));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("unavailable", "认证请求超时，请稍后重试。"); }
        catch (HttpRequestException) { return new("unavailable", "无法连接校园网认证服务，请确认已连接校园网。"); }
        catch (InvalidDataException) { return new("unavailable", "认证响应异常，请重新打开认证网页。"); }
    }
    public async Task<AuthResult> LogoutAsync(string account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Length > 128 || account.Any(char.IsControl)) throw new InvalidDataException("未获取到当前在线账号。");
        // Match windows/legacy/main.py: POST the displayed userId directly, without
        // relying on page JavaScript globals or a browser session userIndex.
        using var request = new HttpRequestMessage(HttpMethod.Post, Protocol.Logout)
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["userId"] = account }) };
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return new("unavailable", "学校下线服务暂时不可用。");
            var parsed = ParseAuthentication(await ReadBoundedAsync(response.Content, 131072, ct));
            return parsed.Success ? new("success", "学校已确认下线。") : parsed.State == "unavailable"
                ? new("unavailable", "下线请求已发送，但未收到有效结果，请刷新查看学校网页。")
                : new("rejected", "学校未确认下线成功，请刷新网页后重试。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("unavailable", "下线请求超时，请刷新网页确认当前状态。"); }
        catch (HttpRequestException) { return new("unavailable", "无法连接学校下线服务，请稍后重试。"); }
        catch (InvalidDataException) { return new("unavailable", "学校下线响应异常，请刷新网页确认当前状态。"); }
    }
    public static AuthResult ParseAuthentication(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.String)
                return new("unavailable", "未收到有效认证结果，请通过网页确认登录状态。");
            var result = r.GetString();
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            if (result == "success") return new("success", "校园网认证成功。");
            if (result != "fail" && result != "failed" && result != "failure") return new("unavailable", "未收到有效认证结果，请通过网页确认登录状态。");
            if (message.Contains("已经在线") || message.Contains("已在线")) return new("alreadyOnline", "账号已在线；这次响应无法确认登录信息能否用于独立重连。");
            return new("rejected", "学校拒绝了认证，请在网页确认账号、密码及服务选项后重新登录。");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new("unavailable", "未收到有效认证结果，请通过网页确认登录状态。"); }
    }
    public static async Task<string> ReadBoundedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, ct)) > 0) { if (buffer.Length + count > limit) throw new InvalidDataException("响应过大"); buffer.Write(bytes, 0, count); }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public void Dispose() => http.Dispose();
}
