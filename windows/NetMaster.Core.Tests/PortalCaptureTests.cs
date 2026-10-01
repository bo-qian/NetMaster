using System.Text.Json;
using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class PortalCaptureTests
{
    private const string Source = "http://10.10.9.9/eportal/index.jsp";
    private static string Request(string id = "one", string session = "active", string url = Protocol.Login, string payload = "userId=fixture&password=fake") => JsonSerializer.Serialize(new { kind = "request", session, id, url, method = "POST", payload });
    private static string Response(string id, string body) => JsonSerializer.Serialize(new { kind = "response", session = "active", id, response = body });

    [Fact]
    public void CapturesBeforeResponseAndRetainsCandidateWhenResponseCannotBeRead()
    {
        var capture = new PortalCapture("active");
        Assert.Equal(CaptureUpdate.Request, capture.Accept(Source, Request()));
        Assert.NotNull(capture.Profile); Assert.False(capture.Profile.Confirmed);
        Assert.Equal(CaptureUpdate.Response, capture.Accept(Source, Response("one", "")));
        Assert.NotNull(capture.Profile); Assert.False(capture.Profile.Confirmed);
        Assert.Equal("unavailable", capture.Authentication!.State);
    }

    [Fact]
    public void OnlyMatchingLatestSuccessfulResponseConfirmsCredentials()
    {
        var capture = new PortalCapture("active");
        capture.Accept(Source, Request()); capture.Accept(Source, Request("two", payload: "userId=second&password=fake"));
        Assert.Equal(CaptureUpdate.None, capture.Accept(Source, Response("one", "{\"result\":\"success\"}")));
        Assert.False(capture.Profile!.Confirmed);
        Assert.Equal(CaptureUpdate.Response, capture.Accept(Source, Response("two", "{\"result\":\"success\"}")));
        Assert.True(capture.Profile.Confirmed); Assert.NotNull(capture.Profile.VerifiedAt);
        Assert.Equal(CaptureUpdate.None, capture.Accept(Source, Request("two")));
        Assert.True(capture.Profile.Confirmed);
    }

    [Theory]
    [InlineData("http://example.com", "active", Protocol.Login, "userId=fixture&password=fake")]
    [InlineData(Source, "cancelled", Protocol.Login, "userId=fixture&password=fake")]
    [InlineData(Source, "active", "http://10.10.9.9/eportal/InterFace.do?method=logout", "userId=fixture&password=fake")]
    [InlineData(Source, "active", Protocol.Login, "userIndex=fixture")]
    public void RejectsOtherOriginsSessionsLogoutAndIncompleteBodies(string source, string session, string url, string payload)
    {
        var capture = new PortalCapture("active");
        Assert.Equal(CaptureUpdate.None, capture.Accept(source, Request(session: session, url: url, payload: payload)));
        Assert.Null(capture.Profile);
    }

    [Fact]
    public void AlreadyOnlineAndMalformedMessagesCannotProveNewCredentials()
    {
        var capture = new PortalCapture("active"); capture.Accept(Source, Request());
        capture.Accept(Source, Response("one", "{\"result\":\"fail\",\"message\":\"已经在线\"}"));
        Assert.False(capture.Profile!.Confirmed); Assert.Equal("alreadyOnline", capture.Authentication!.State);
        Assert.Equal(CaptureUpdate.None, capture.Accept(Source, "[]"));
        Assert.Equal(CaptureUpdate.None, capture.Accept(Source, new string('x', 400001)));
        Assert.DoesNotContain("fake", capture.Profile.ToString());
    }
}
