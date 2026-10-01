using System.Text.Json;

namespace NetMaster.Core;

public enum CaptureUpdate { None, Request, Response }

// Candidate credentials stay in memory until the user explicitly saves them.
public sealed class PortalCapture(string session)
{
    private string? requestId;
    public LoginProfile? Profile { get; private set; }
    public AuthResult? Authentication { get; private set; }

    public CaptureUpdate Accept(string source, string json)
    {
        if (!Protocol.IsPortalSource(source) || json.Length > 400000) return CaptureUpdate.None;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? Field(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (Field("session") != session || Field("id") is not { Length: > 0 and <= 100 } id) return CaptureUpdate.None;
            if (Field("kind") == "request")
            {
                if (id == requestId || !Protocol.IsLogin(Field("url") ?? "", Field("method") ?? "")) return CaptureUpdate.None;
                var profile = new LoginProfile { Payload = Field("payload") ?? "" };
                profile.Validate();
                requestId = id; Profile = profile; Authentication = null;
                return CaptureUpdate.Request;
            }
            if (Field("kind") == "response" && id == requestId && Profile is not null && Authentication is null)
            {
                var response = Field("response") ?? "";
                Authentication = NetworkService.ParseAuthentication(response.Length <= 131072 ? response : "");
                Profile = Profile with { Confirmed = Authentication.Success, VerifiedAt = Authentication.Success ? DateTimeOffset.Now : null };
                return CaptureUpdate.Response;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or InvalidDataException or UriFormatException) { }
        return CaptureUpdate.None;
    }
}
