namespace NetMaster.Core;

// Authentication responses describe the server result; operation messages also
// describe why the request was sent. A test never implies that a profile was saved.
public static class AuthenticationText
{
    public static string ConfigurationTest(AuthResult result, bool candidate)
    {
        var subject = candidate ? "本次校园网配置" : "已保存的校园网配置";
        return result.State switch
        {
            "success" => subject + "测试成功。" + (candidate ? "尚未保存，请保存并启用守护。" : ""),
            "alreadyOnline" => subject + "测试未完成：账号已在线，未取得新的验证结果。",
            "rejected" => subject + "测试失败：" + result.Message,
            _ => subject + "测试未完成：" + result.Message
        };
    }

    public static string Reconnect(AuthResult result) => result.State switch
    {
        "success" => "自动重连认证成功，正在确认互联网连接。",
        "alreadyOnline" => "自动重连检查：账号已在线，正在确认互联网连接。",
        "rejected" => "自动重连认证失败：" + result.Message,
        _ => "自动重连未完成：" + result.Message
    };
}
