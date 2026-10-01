using System.Net;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class NetworkTests
{
    [Fact]
    public async Task LoginUsesPythonBrowserHeadersAndReplaysEncodedPayloadUnchanged()
    {
        var calls = 0;
        var profile = StorageTests.FakeProfile with { Payload = "userId=fixture%40school&password=fake%2B%26&queryString=a%253Db" };
        using var network = new NetworkService(new AsyncHandler(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Protocol.Login, request.RequestUri!.AbsoluteUri);
            Assert.Equal(profile.Payload, await request.Content!.ReadAsStringAsync());
            Assert.Contains("Chrome/120.0.0.0", request.Headers.UserAgent.ToString());
            Assert.Contains("Edg/120.0.0.0", request.Headers.UserAgent.ToString());
            Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal("UTF-8", request.Content.Headers.ContentType.CharSet);
            Assert.Equal(Protocol.Portal, Assert.Single(request.Headers.GetValues("Origin")));
            Assert.Equal(Protocol.Portal + "/eportal/index.jsp", request.Headers.Referrer!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"fail\",\"message\":\"已经在线\"}") };
        }));
        var result = await network.LoginAsync(profile, default);
        Assert.Equal("alreadyOnline", result.State);
        Assert.False(result.Success);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LogoutPostsPythonUserIdContractWithoutBrowserSession()
    {
        var calls = 0;
        using var network = new NetworkService(new AsyncHandler(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Protocol.Logout, request.RequestUri!.AbsoluteUri);
            Assert.Equal("userId=fixture%40school", await request.Content!.ReadAsStringAsync());
            Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);
            Assert.False(request.Headers.Contains("Cookie"));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"success\"}") };
        }));
        Assert.True((await network.LogoutAsync("fixture@school", default)).Success);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}", "unavailable")]
    [InlineData("<html>fixture</html>", "unavailable")]
    [InlineData("{\"result\":\"fail\",\"message\":\"fixture sensitive account\"}", "rejected")]
    [InlineData("{\"result\":\"fail\",\"message\":\"已经在线\"}", "rejected")]
    public async Task LogoutRequiresSuccessAndDoesNotExposeRawResponse(string body, string state)
    {
        using var network = new NetworkService(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var result = await network.LogoutAsync("fixture", default);
        Assert.Equal(state, result.State); Assert.DoesNotContain("fixture sensitive", result.Message);
    }

    [Fact]
    public async Task LogoutWithoutAccountMakesNoRequest()
    {
        using var network = new NetworkService(new Handler(_ => throw new InvalidOperationException("Must not call HTTP")));
        await Assert.ThrowsAsync<InvalidDataException>(() => network.LogoutAsync("", default));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return respond(request); }
    }
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
    [InlineData("http://10.10.9.9:8080/eportal/InterFace.do?method=login", "POST", true)]
    [InlineData("http://10.10.9.9:8081/eportal/InterFace.do?method=login", "POST", false)]
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
