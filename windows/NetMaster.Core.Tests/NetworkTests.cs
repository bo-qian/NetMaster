using System.Net;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class NetworkTests
{
    [Theory]
    [InlineData("{\"result\":\"success\"}", "success")]
    [InlineData("{\"result\":\"fail\",\"message\":\"已经在线\"}", "alreadyOnline")]
    [InlineData("{\"result\":\"fail\",\"message\":\"fixture sensitive server text\"}", "rejected")]
    [InlineData("{}", "unavailable")]
    [InlineData("[]", "unavailable")]
    [InlineData("null", "unavailable")]
    [InlineData("{\"result\":42}", "unavailable")]
    [InlineData("{\"result\":\"unknown\"}", "unavailable")]
    [InlineData("<html>fixture</html>", "unavailable")]
    public void AuthenticationRequiresKnownResultAndDoesNotExposeResponse(string body, string state)
    {
        var result = NetworkService.ParseAuthentication(body);
        Assert.Equal(state, result.State); Assert.DoesNotContain("sensitive server text", result.Message);
    }

    [Theory]
    [InlineData("http://10.10.9.9/eportal/InterFace.do?method=login", "POST", true)]
    [InlineData("http://10.10.9.9/eportal/InterFace.do?method=login", "GET", false)]
    [InlineData("http://10.10.9.9:8080/eportal/InterFace.do?method=login", "POST", false)]
    [InlineData("http://example.com/eportal/InterFace.do?method=login", "POST", false)]
    [InlineData("http://10.10.9.9/eportal/InterFace.do?method=logout", "POST", false)]
    public void CaptureIsRestrictedToSchoolLogin(string url, string method, bool expected) => Assert.Equal(expected, Protocol.IsLogin(url, method));

    [Fact]
    public async Task RelativeRedirectsDoNotCrashOrProveCampusAuthentication()
    {
        using var network = new NetworkService(new Handler(request => request.RequestUri!.Host == "10.10.9.9" ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/login", UriKind.Relative) } }), () => true);
        Assert.Equal("uncertain", (await network.DetectAsync(default)).State);
    }

    [Fact]
    public async Task SchoolRedirectIsRecognizedOnlyWhenNoProbeProvesOnline()
    {
        using var network = new NetworkService(new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://10.10.9.9/eportal/") } }), () => true);
        Assert.Equal("authentication", (await network.DetectAsync(default)).State);
        using var online = new NetworkService(new Handler(request => request.RequestUri!.Host == "cp.cloudflare.com" ? new(HttpStatusCode.NoContent) : new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://10.10.9.9/") } }), () => true);
        Assert.Equal("online", (await online.DetectAsync(default)).State);
    }

    [Fact]
    public async Task NoAdapterDoesNotMakeHttpRequests()
    {
        using var network = new NetworkService(new Handler(_ => throw new InvalidOperationException("Must not call HTTP")), () => false);
        Assert.Equal("offline", (await network.DetectAsync(default)).State);
    }

    [Fact]
    public async Task OversizedAndCancelledResponsesAreBounded()
    {
        using var content = new StringContent(new string('x', 4097));
        await Assert.ThrowsAsync<InvalidDataException>(() => NetworkService.ReadBoundedAsync(content, 4096, default));
        using var reader = new StringReader(new string('x', 100001));
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadMessageAsync(reader, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var network = new NetworkService(new Handler(_ => new(HttpStatusCode.NoContent)), () => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => network.DetectAsync(cancellation.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
