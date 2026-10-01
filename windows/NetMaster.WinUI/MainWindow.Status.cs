namespace NetMaster.WinUI;

public sealed partial class MainWindow
{
    private void SetGuardianStatus(string text)
    {
        GuardStatus.Text = text == "运行中" ? "守护运行中" : text;
        GuardStatus.Tone = text switch
        {
            "运行中" => StatusTone.Success,
            "重连中" => StatusTone.Working,
            "后台未连接" => StatusTone.Critical,
            "未配置" or "已停止守护" => StatusTone.Neutral,
            _ => StatusTone.Caution
        };
    }
}
