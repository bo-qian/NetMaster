using System.Net;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class SpeedTests
{
    [Fact]
    public async Task CompleteTestReportsMeasuredValuesAndOnlyExpectedByteCounts()
    {
        var handler = new SpeedHandler();
        var result = await new SpeedService(() => handler).RunAsync(new Progress<string>(), default);
        Assert.True(result.DownloadMbps > 0); Assert.True(result.UploadMbps > 0); Assert.True(result.LatencyMs >= 0);
        Assert.Equal(5_000_000, handler.UploadBytes); Assert.Equal(5, handler.DownloadRequests);
    }

    [Fact]
    public async Task ShortDownloadKeepsLatencyAndDoesNotFabricateThroughput()
    {
        var handler = new SpeedHandler { ShortDownload = true };
        var result = await new SpeedService(() => handler).RunAsync(new Progress<string>(), default);
        Assert.NotNull(result.LatencyMs); Assert.Null(result.DownloadMbps); Assert.Null(result.UploadMbps);
        Assert.Equal(0, handler.UploadBytes);
    }

    [Fact]
    public async Task CancellationReturnsNoInventedResults()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new SpeedService(() => new SpeedHandler()).RunAsync(new Progress<string>(), cancellation.Token);
        Assert.Null(result.DownloadMbps); Assert.Null(result.UploadMbps); Assert.Null(result.LatencyMs);
        Assert.Contains("取消", result.Message);
    }

    private sealed class SpeedHandler : HttpMessageHandler
    {
        public bool ShortDownload;
        public long UploadBytes;
        public int DownloadRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Post)
            {
                UploadBytes += (await request.Content!.ReadAsByteArrayAsync(ct)).Length;
                return new(HttpStatusCode.OK);
            }
            DownloadRequests++;
            var bytes = request.RequestUri!.Query.Contains("20000000") ? ShortDownload ? 1000 : 20_000_000 : 0;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };
        }
    }
}
