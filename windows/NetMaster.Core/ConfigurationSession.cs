namespace NetMaster.Core;

public enum ConfigurationStage { Idle, Preparing, SigningOut, WaitingForLogin, Captured, Validating, ReadyToSave, Saving, SaveFailed }

// A new session owns only its new candidate. Saved credentials are never a
// fallback for validation, and status polling cannot change the session.
public sealed class ConfigurationSession
{
    public ConfigurationStage Stage { get; private set; }
    public bool Active => Stage != ConfigurationStage.Idle;
    public LoginProfile? Candidate { get; private set; }
    public string? Message { get; private set; }
    public bool Error { get; private set; }
    public bool CanValidate => Active && Candidate is not null && Stage is not (ConfigurationStage.Preparing or ConfigurationStage.SigningOut or ConfigurationStage.Validating or ConfigurationStage.Saving);
    public bool CanSave => Active && Candidate?.Confirmed == true && Stage is ConfigurationStage.ReadyToSave or ConfigurationStage.SaveFailed;

    public void Begin() { Candidate = null; Message = null; Error = false; Stage = ConfigurationStage.Preparing; }
    public void AwaitLogin() { if (!Active) return; Candidate = null; Message = null; Error = false; Stage = ConfigurationStage.WaitingForLogin; }
    public void SigningOut() { if (Active) { Candidate = null; Message = null; Error = false; Stage = ConfigurationStage.SigningOut; } }
    public void Receive(LoginProfile profile)
    {
        if (!Active || Stage == ConfigurationStage.Saving) return;
        Candidate = profile; Message = null; Error = false;
        Stage = profile.Confirmed ? ConfigurationStage.ReadyToSave : ConfigurationStage.Captured;
    }
    public bool Validate() { if (!CanValidate) return false; Stage = ConfigurationStage.Validating; Message = null; Error = false; return true; }
    public void Authentication(AuthResult result, bool fromTest = false)
    {
        if (!Active || Candidate is null || Stage == ConfigurationStage.Saving) return;
        if (result.Success) Candidate = Candidate with { Confirmed = true, VerifiedAt = DateTimeOffset.Now };
        else if (result.State == "rejected") Candidate = Candidate with { Confirmed = false, VerifiedAt = null };
        Stage = Candidate.Confirmed ? ConfigurationStage.ReadyToSave : ConfigurationStage.Captured;
        Message = result.State == "alreadyOnline"
            ? Candidate.Confirmed ? fromTest ? "本次测试未取得新的验证结果，之前验证仍有效，可保存。" : "本次信息已验证，可保存；学校返回账号已在线。"
                : fromTest ? result.Message : "账号已在线，本次信息尚未验证。请重新认证。"
            : result.Success ? fromTest ? result.Message : null
                : result.Message + (fromTest && Candidate.Confirmed ? "之前验证仍有效，可保存。" : "");
        Error = !result.Success && result.State != "alreadyOnline";
    }
    public bool Save() { if (!CanSave) return false; Stage = ConfigurationStage.Saving; Message = null; Error = false; return true; }
    public void Fail(string message)
    {
        if (!Active) return;
        Stage = Stage == ConfigurationStage.Saving ? ConfigurationStage.SaveFailed
            : Candidate?.Confirmed == true ? ConfigurationStage.ReadyToSave
            : Candidate is not null ? ConfigurationStage.Captured : ConfigurationStage.WaitingForLogin;
        Message = message; Error = true;
    }
    public void End() { Stage = ConfigurationStage.Idle; Candidate = null; Message = null; Error = false; }
}
