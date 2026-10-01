using System.Net;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class SpeedTests
{
    private static readonly SpeedTestOptions Limits = new(TimeSpan.FromMilliseconds(90), 1_000_000);
    private static Task<NetworkResult> Online(CancellationToken ct) => Task.FromResult(new NetworkResult("online", DateTimeOffset.Now, "fixture"));
    private sealed class Capture : IProgress<SpeedProgress>
    {
        public List<SpeedProgress> Values { get; } = new();
        public void Report(SpeedProgress value) => Values.Add(value);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("authentication")]
    [InlineData("uncertain")]
    public async Task FreshOnlineCheckBlocksTransfersAndManualPause(string state)
    {
        bool opened = false, paused = false;
        var progress = new Capture();
        var service = new SpeedService(() => { opened = true; return new SpeedHandler(); },
            _ => Task.FromResult(new NetworkResult(state, DateTimeOffset.Now, "fixture")), Limits);
        var result = await service.RunAsync(progress, default, _ => { paused = true; return Task.FromResult(true); });
        Assert.False(opened); Assert.False(paused);
        Assert.Equal("notOnline", result.State); Assert.Null(result.Download); Assert.Null(result.Upload);
        Assert.Contains("正在检查", progress.Values[0].Message);
    }

    [Fact]
    public async Task CompleteTestUsesConcurrentTransfersWithinBudgetAndExcludesWarmup()
    {
        var handler = new SpeedHandler(); var progress = new Capture();
        var result = await new SpeedService(() => handler, Online, Limits).RunAsync(progress, default);
        Assert.Equal("completed", result.State);
        Assert.True(handler.PeakConcurrency > 1);
        Assert.InRange(handler.DownloadBytes, 1, Limits.MaxBytesPerDirection);
        Assert.InRange(handler.UploadBytes, 1, Limits.MaxBytesPerDirection);
        Assert.True(handler.NoCache);
        Assert.NotNull(result.Download); Assert.NotNull(result.Upload);
        foreach (var value in new[] { result.Download!, result.Upload! })
        {
            Assert.True(value.AverageMBps > 0); Assert.True(value.Samples > 0);
            Assert.InRange(value.AverageMBps, value.MinimumMBps, value.MaximumMBps);
        }
        Assert.Equal(handler.DownloadBytes - 4 * 32_768, result.Download!.Bytes);
        Assert.Equal(handler.UploadBytes - 4 * 32_768, result.Upload!.Bytes);
        Assert.NotNull(result.LatencyMs);
        Assert.Contains(progress.Values, p => p.Download is not null);
        Assert.Contains(progress.Values, p => p.Upload is not null);
    }

    [Fact]
    public async Task ShortDownloadKeepsLatencyAndDoesNotFabricateThroughput()
    {
        var handler = new SpeedHandler { ShortDownload = true };
        var result = await new SpeedService(() => handler, Online, Limits).RunAsync(new Capture(), default);
        Assert.Equal("unavailable", result.State);
        Assert.NotNull(result.LatencyMs); Assert.Null(result.Download); Assert.Null(result.Upload);
        Assert.Equal(0, handler.UploadBytes);
    }

    [Fact]
    public async Task FailedUploadAcknowledgementDoesNotCountBufferedBytes()
    {
        var handler = new SpeedHandler { RejectUpload = true };
        var result = await new SpeedService(() => handler, Online, Limits).RunAsync(new Capture(), default);
        Assert.NotNull(result.Download); Assert.Null(result.Upload); Assert.Equal("unavailable", result.State);
    }

    [Fact]
    public async Task CancellationDuringOnlineCheckDoesNotStartTransfers()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool opened = false;
        var service = new SpeedService(() => { opened = true; return new SpeedHandler(); }, async ct =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return await Online(ct); }, Limits);
        var running = service.RunAsync(new Capture(), cancellation.Token);
        await entered.Task; cancellation.Cancel();
        var result = await running;
        Assert.False(opened); Assert.Equal("cancelled", result.State);
        Assert.Null(result.Download); Assert.Null(result.Upload); Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task CancellationAfterDownloadRetainsOnlyCompletedSamples()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnUpload(cancellation);
        var result = await new SpeedService(() => new SpeedHandler(), Online, Limits).RunAsync(progress, cancellation.Token);
        Assert.Equal("cancelled", result.State); Assert.NotNull(result.Download); Assert.Null(result.Upload);
    }

    private sealed class CancelOnUpload(CancellationTokenSource cancellation) : IProgress<SpeedProgress>
    { public void Report(SpeedProgress value) { if (value.Message.Contains("上传")) cancellation.Cancel(); } }

    private sealed class SpeedHandler : HttpMessageHandler
    {
        public bool ShortDownload, RejectUpload, NoCache = true;
        public long UploadBytes, DownloadBytes;
        private int concurrent;
        public int PeakConcurrency;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var active = Interlocked.Increment(ref concurrent);
            PeakConcurrency = Math.Max(PeakConcurrency, active);
            try
            {
                await Task.Delay(8, ct);
                NoCache &= request.Headers.CacheControl?.NoCache == true && request.Headers.CacheControl?.NoStore == true;
                if (request.Method == HttpMethod.Post)
                {
                    Interlocked.Add(ref UploadBytes, (await request.Content!.ReadAsByteArrayAsync(ct)).Length);
                    return new(RejectUpload ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StringContent("") };
                }
                int bytes = int.Parse(Protocol.Form(request.RequestUri!.Query.TrimStart('?'))["bytes"]);
                Interlocked.Add(ref DownloadBytes, bytes);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[ShortDownload && bytes > 0 ? 1000 : bytes]) };
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
    }
}
