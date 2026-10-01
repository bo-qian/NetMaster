using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NetMaster.Core;

public sealed record SpeedMeasurement(double AverageMBps, double MinimumMBps, double MaximumMBps, int Samples, long Bytes);
public sealed record SpeedProgress(string Message, SpeedMeasurement? Download = null, SpeedMeasurement? Upload = null, double? LatencyMs = null);
public sealed record SpeedResult(SpeedMeasurement? Download, SpeedMeasurement? Upload, double? LatencyMs, string Message, string State);
public sealed record SpeedTestOptions(TimeSpan DirectionDuration, long MaxBytesPerDirection)
{
    // Application payload, including warm-up; HTTP/TLS overhead is additional.
    public static SpeedTestOptions Default { get; } = new(TimeSpan.FromSeconds(8), 256_000_000);
}

public sealed class SpeedService(Func<HttpMessageHandler>? handlerFactory = null,
    Func<CancellationToken, Task<NetworkResult>>? onlineCheck = null, SpeedTestOptions? options = null)
{
    private const int Connections = 4, MinChunk = 32_768, MaxChunk = 16_000_000;

    public async Task<SpeedResult> RunAsync(IProgress<SpeedProgress> progress, CancellationToken ct,
        Func<CancellationToken, Task<bool>>? prepareMeasurement = null)
    {
        var limits = options ?? SpeedTestOptions.Default;
        if (limits.DirectionDuration <= TimeSpan.Zero || limits.DirectionDuration > TimeSpan.FromSeconds(30)
            || limits.MaxBytesPerDirection < Connections * MinChunk * 2 || limits.MaxBytesPerDirection > 512_000_000)
            throw new ArgumentOutOfRangeException(nameof(options));
        SpeedMeasurement? download = null, upload = null;
        double? latency = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new("正在检查网络连接…"));
            using var detector = onlineCheck is null ? new NetworkService() : null;
            using var check = CancellationTokenSource.CreateLinkedTokenSource(ct);
            check.CancelAfter(TimeSpan.FromSeconds(8));
            NetworkResult network;
            try { network = onlineCheck is null ? await detector!.DetectAsync(check.Token) : await onlineCheck(check.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { return new(null, null, null, "暂未确认互联网连接，无法开始测速。请检查网络后重试。", "notOnline"); }
            if (network.State != "online")
                return new(null, null, null, network.State is "offline" or "authentication"
                    ? "当前未连接互联网，不能测速。请联网后重试。"
                    : "暂未确认互联网连接，无法开始测速。请检查网络后重试。", "notOnline");
            ct.ThrowIfCancellationRequested();
            progress.Report(new("准备测速…"));
            if (prepareMeasurement is not null && !await prepareMeasurement(ct))
                return new(null, null, null, "暂时无法开始测速，请稍后重试。", "unavailable");
            ct.ThrowIfCancellationRequested();
            using var http = new HttpClient(handlerFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = Connections })
                { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            progress.Report(new("正在测量延迟…"));
            var times = new List<double>();
            using var latencyPhase = CancellationTokenSource.CreateLinkedTokenSource(ct);
            latencyPhase.CancelAfter(TimeSpan.FromSeconds(12));
            for (int i = 0; i < 4; i++)
            {
                var clock = Stopwatch.StartNew();
                using var response = await http.GetAsync(DownloadUrl(0), HttpCompletionOption.ResponseHeadersRead, latencyPhase.Token);
                response.EnsureSuccessStatusCode();
                if ((await NetworkService.ReadBoundedAsync(response.Content, 4096, latencyPhase.Token)).Length != 0) throw new InvalidDataException();
                if (i > 0) times.Add(clock.Elapsed.TotalMilliseconds);
            }
            latency = times.Order().ElementAt(1);
            download = await MeasureAsync(false, value => { download = value; progress.Report(new("正在测量下载…", download, null, latency)); });
            upload = await MeasureAsync(true, value => { upload = value; progress.Report(new("正在测量上传…", download, upload, latency)); });
            return new(download, upload, latency, "测速完成 · 平均 / 最高 / 最低为本轮传输速度，受服务器线路影响。", "completed");

            async Task<SpeedMeasurement> MeasureAsync(bool uploading, Action<SpeedMeasurement?> report)
            {
                report(null);
                // Reuse incompressible data; count uploads only after the server acknowledges
                // each complete request, never when bytes merely enter an OS send buffer.
                var payload = uploading ? new byte[MaxChunk] : null;
                if (payload is not null) RandomNumberGenerator.Fill(payload);
                using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct);
                phase.CancelAfter(limits.DirectionDuration + TimeSpan.FromSeconds(4));
                long transferred = 0, measured = 0;
                double seconds = 0;
                var samples = new List<double>();
                var chunk = MinChunk;
                var duration = Stopwatch.StartNew();
                bool warmup = true;
                while (warmup || duration.Elapsed < limits.DirectionDuration)
                {
                    ct.ThrowIfCancellationRequested();
                    int bytes = (int)Math.Min(chunk, (limits.MaxBytesPerDirection - transferred) / Connections);
                    if (bytes < MinChunk) break;
                    var clock = Stopwatch.StartNew();
                    using var round = CancellationTokenSource.CreateLinkedTokenSource(phase.Token);
                    var tasks = Enumerable.Range(0, Connections).Select(async _ =>
                    {
                        try { await TransferAsync(http, uploading, payload, bytes, round.Token); }
                        catch { round.Cancel(); throw; }
                    }).ToArray();
                    // A failed / truncated round never contributes to throughput statistics.
                    await Task.WhenAll(tasks);
                    var elapsed = clock.Elapsed.TotalSeconds;
                    long total = (long)bytes * Connections;
                    transferred += total;
                    var rate = total / elapsed / 1_000_000;
                    if (!warmup)
                    {
                        measured += total; seconds += elapsed; samples.Add(rate);
                        report(new(measured / seconds / 1_000_000, samples.Min(), samples.Max(), samples.Count, measured));
                    }
                    // Aim for ~1 s per concurrent round to avoid tiny-buffer spikes.
                    chunk = (int)Math.Clamp(total / elapsed / Connections, MinChunk, MaxChunk);
                    warmup = false;
                }
                if (samples.Count == 0) throw new InvalidDataException();
                return new(measured / seconds / 1_000_000, samples.Min(), samples.Max(), samples.Count, measured);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return new(download, upload, latency, "测速已取消，保留已完成的有效样本。", "cancelled"); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException)
        { return new(download, upload, latency, "测速中断：网络或测速服务不可用。保留已完成的有效样本。", "unavailable"); }
    }

    private static string DownloadUrl(int bytes) => $"https://speed.cloudflare.com/__down?bytes={bytes}&netmaster={Guid.NewGuid():N}";

    private static async Task TransferAsync(HttpClient http, bool uploading, byte[]? payload, int bytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(uploading ? HttpMethod.Post : HttpMethod.Get,
            uploading ? "https://speed.cloudflare.com/__up" : DownloadUrl(bytes));
        if (uploading) request.Content = new ByteArrayContent(payload!, 0, bytes);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (uploading) { await NetworkService.ReadBoundedAsync(response.Content, 4096, ct); return; }
        if ((response.Content.Headers.ContentLength is { } length && length != bytes) || response.Content.Headers.ContentEncoding.Count != 0)
            throw new InvalidDataException();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[65_536];
        int count; long received = 0;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            received += count;
            if (received > bytes) throw new InvalidDataException();
        }
        if (received != bytes) throw new InvalidDataException();
    }
}
