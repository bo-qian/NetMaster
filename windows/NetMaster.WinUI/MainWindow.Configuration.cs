using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using NetMaster.Core;

namespace NetMaster.WinUI;

public sealed partial class MainWindow
{
    private void UpdateConfiguration()
    {
        if (ConfigurationState is null) return;
        bool saved = snapshot?.HasProfile == true;
        var stage = configuration.Stage;
        string instruction = stage switch
        {
            ConfigurationStage.Preparing => "正在清除旧信息并准备认证页面…",
            ConfigurationStage.SigningOut => "正在下线，等待学校确认…",
            ConfigurationStage.WaitingForLogin => "请在下方网页输入账号和密码，完成登录。",
            ConfigurationStage.Captured => "已获取本次信息。等待认证结果，或点击测试本次配置。",
            ConfigurationStage.Validating => "正在验证本次登录信息…",
            ConfigurationStage.ReadyToSave => "本次登录信息已验证，点击保存并启用守护。",
            ConfigurationStage.Saving => "正在保存登录信息并启用守护…",
            ConfigurationStage.SaveFailed => "登录信息尚未保存，点击重试保存。",
            _ => saved ? "需要更换登录信息时，点击重新配置。" : "点击开始配置，之后即可自动恢复校园网连接。"
        };
        ConfigurationState.Text = stage switch
        {
            ConfigurationStage.Preparing => "准备配置",
            ConfigurationStage.SigningOut => "正在重新认证",
            ConfigurationStage.WaitingForLogin => "等待登录",
            ConfigurationStage.Captured => "等待验证",
            ConfigurationStage.Validating => "正在验证",
            ConfigurationStage.ReadyToSave => "验证成功，等待保存",
            ConfigurationStage.Saving => "正在保存",
            ConfigurationStage.SaveFailed => "保存失败，等待重试",
            _ => saved ? "配置已完成" : "尚未配置"
        };
        ConfigurationAccount.Text = configurationActive
            ? candidate is null ? "本次配置账号：尚未获取" : "本次配置账号：" + Protocol.Mask(candidate.Account)
            : saved ? "当前配置账号：" + snapshot!.Account : "配置一次，之后由 NetMaster 自动检测并恢复校园网连接。";
        ConfigurationSteps.Text = configurationActive
            ? candidate?.Confirmed == true ? "① 已获取　→　② 已确认　→　③ 保存并启用守护"
            : candidate is not null ? "① 已获取　→　② 确认有效　→　③ 保存并启用守护"
            : "① 获取登录信息　→　② 确认有效　→　③ 保存并启用守护"
            : saved ? "① 已获取　→　② 已确认　→　③ 已保存" : "① 获取登录信息　→　② 确认有效　→　③ 保存并启用守护";
        ConfigurationMessage.Text = configuration.Message ?? instruction;
        if (configurationActive && configuration.Error && ConfigurationPanel.Visibility == Visibility.Visible)
        {
            Notice(configuration.Message ?? instruction, true, "配置未完成");
        }
        LoginState.Text = configurationActive ? ConfigurationState.Text : saved ? "配置已完成" : "尚未配置";
        LoginAccount.Text = configurationActive
            ? candidate is null ? "本次账号：尚未获取" : "本次账号：" + Protocol.Mask(candidate.Account)
            : "当前配置账号：" + (snapshot?.Account ?? "未获取");
        LoginSaved.Text = configurationActive ? "本次登录信息：尚未保存" : saved ? "登录信息：已加密保存" : "登录信息：未保存";
        LoginExplanation.Text = configurationActive ? stage == ConfigurationStage.ReadyToSave ? "保存成功后启用后台守护。" : "仅保存本次验证成功的登录信息。"
            : saved ? $"自动重连：{(snapshot!.Settings.AutoReconnect ? "已启用" : "已暂停")}。后台：{(backgroundRemoved ? "已停止守护" : snapshot.Guardian)}。"
            : "尚未配置自动重连。";
        if (configurationActive) PortalStatus.Text = stage == ConfigurationStage.WaitingForLogin ? "请在应用内完成登录；外部浏览器无法获取配置所需的信息。" : configuration.Message ?? instruction;
        else if (saved) PortalStatus.Text = snapshot!.Settings.AutoReconnect && !backgroundRemoved ? "配置已保存。关闭主窗口后后台守护仍会继续。" : "配置已保存，自动重连当前未运行。";
        StartConfigurationButton.Content = OverviewConfigureButton.Content = saved ? "重新配置" : "开始配置";
        StartConfigurationButton.Visibility = configurationActive ? Visibility.Collapsed : Visibility.Visible;
        StartConfigurationButton.IsEnabled = !busy && businessReady;
        CancelConfigurationButton.Visibility = configurationActive ? Visibility.Visible : Visibility.Collapsed;
        CancelConfigurationButton.IsEnabled = !busy;
        RestartAuthenticationButton.Visibility = configurationActive && candidate?.Confirmed != true ? Visibility.Visible : Visibility.Collapsed;
        RestartAuthenticationButton.Content = logoutAttempted && configuration.Error ? "重试认证" : "重新认证";
        RestartAuthenticationButton.IsEnabled = !busy && portalReady;
        ValidateButton.Content = configurationActive ? "测试本次配置" : "测试当前配置";
        ValidateButton.IsEnabled = !busy && (configurationActive ? configuration.CanValidate : saved);
        SaveProfileButton.Content = !configurationActive && saved ? "已保存" : stage == ConfigurationStage.SaveFailed ? "重试保存" : "保存并启用守护";
        SaveProfileButton.IsEnabled = !busy && configuration.CanSave;
        SaveProfileButton.Style = configuration.CanSave ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        StartConfigurationButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        LoginPanel.Visibility = configurationExpanded && ConfigurationDetailsVisible ? Visibility.Visible : Visibility.Collapsed;
        LoginInfoCard.Visibility = ConfigurationDetailsVisible ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationActions.Visibility = ConfigurationDetailsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (configurationActive)
        {
            OverviewAccount.Text = "本次配置进行中，尚未保存";
            GuardStatus.Text = "配置中，重连暂缓";
        }
        UpdateResponsiveLayout();
    }

    private async void StartConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !businessReady || configurationActive || settingsOpen) return;
        SetBusy(true);
        try
        {
            bool saved = snapshot?.HasProfile == true;
            logoutApproved = false;
            if (saved || snapshot?.Network.State == "online")
            {
                var dialog = new ContentDialog
                {
                    Title = saved ? "重新配置" : "开始配置",
                    Content = "将清除原登录信息，不保留备份；取消配置后也不会恢复。若校园网网页已在线，将退出当前登录，网络会暂时断开。",
                    PrimaryButtonText = "开始配置", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
                logoutApproved = true;
            }
            ClearNotice(); ++captureGeneration; configuration.Begin(); configurationExpanded = true;
            portalOnlineAccount = null; portalCapture = null; logoutAttempted = false;
            captureSession = Guid.NewGuid().ToString("N");
            UpdateConfiguration();
            var reply = await SendAsync(new Command { Name = "beginConfiguration" }, false);
            if (reply?.Ok != true) { FinishConfiguration(); UpdateConfiguration(); Notice(reply?.Message ?? "未确认旧信息已清除，请重试开始配置。", true); return; }
            configuration.AwaitLogin(); portalCapture = new(captureSession);
            bool wasReady = portalReady;
            await EnsurePortalAsync();
            if (wasReady && portalReady) { await InstallCaptureScriptAsync(); PortalWeb.CoreWebView2.Navigate(Protocol.Portal); }
        }
        finally { SetBusy(false); }
        await PrepareAuthenticationAsync();
    }

    private async void CancelConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !configurationActive) return;
        SetBusy(true); ClearNotice();
        try
        {
            var reply = await SendAsync(new Command { Name = "cancelConfiguration" }, false);
            if (reply?.Ok != true) { configuration.Fail(reply?.Message ?? "取消未得到后台确认，请重试。"); return; }
            FinishConfiguration(); configurationExpanded = false;
            if (portalReady) PortalWeb.CoreWebView2.Stop();
            await SetLoginPageAsync(false);
            Notice("已取消配置，当前未配置自动重连。旧登录信息不会恢复。");
        }
        finally { SetBusy(false); }
    }

    private async Task PortalNavigatedAsync(bool success)
    {
        if (!configurationActive || candidate is not null || closed) return;
        var generation = captureGeneration;
        if (!success) { configuration.Fail("网页加载失败，请刷新或检查校园网连接。"); UpdateConfiguration(); return; }
        try
        {
            var account = await ReadPortalAccountAsync();
            if (closed || !configurationActive || generation != captureGeneration || candidate is not null) return;
            portalOnlineAccount = account;
            await PrepareAuthenticationAsync();
        }
        catch { configuration.Fail("无法确认网页状态，请刷新后重试。"); UpdateConfiguration(); }
    }

    private async Task PrepareAuthenticationAsync()
    {
        if (!busy && configurationActive && logoutAttempted && portalOnlineAccount is not null && candidate is null && !configuration.Error)
        {
            configuration.Fail("网页仍显示在线，请点击重试认证。"); UpdateConfiguration(); return;
        }
        if (busy || preparingAuthentication || logoutAttempted || !configurationActive || !portalReady || candidate is not null || portalOnlineAccount is null || settingsOpen) return;
        preparingAuthentication = true; SetBusy(true);
        var generation = captureGeneration;
        try
        {
            if (!logoutApproved)
            {
                var dialog = new ContentDialog { Title = "准备登录", Content = "校园网网页当前已在线。继续将退出当前登录，网络会暂时断开。", PrimaryButtonText = "继续配置", CloseButtonText = "暂不下线", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) { logoutAttempted = true; configuration.Fail("网页仍在线，点击重新认证后可输入新的登录信息。"); return; }
            }
            if (closed || !configurationActive || generation != captureGeneration) return;
            logoutAttempted = true; configuration.SigningOut(); UpdateConfiguration();
            using var network = new NetworkService();
            var result = await network.LogoutAsync(portalOnlineAccount, windowLifetime.Token);
            if (closed || !configurationActive || generation != captureGeneration) return;
            if (!result.Success) { configuration.Fail(result.Message + " 点击重试认证。"); return; }
            ++captureGeneration; portalOnlineAccount = null; portalCapture = new(captureSession);
            configuration.AwaitLogin(); PortalWeb.CoreWebView2.Navigate(Protocol.Portal);
        }
        catch (OperationCanceledException) when (closed) { }
        catch { if (!closed) configuration.Fail("未确认下线成功，请点击重试认证。"); }
        finally { logoutApproved = false; preparingAuthentication = false; SetBusy(false); }
    }

    private async void RestartAuthentication_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !configurationActive || !portalReady || settingsOpen) return;
        SetBusy(true);
        try
        {
            var dialog = new ContentDialog { Title = "重新认证", Content = "将丢弃本次未保存信息；网页已在线时会下线，网络会暂时断开。", PrimaryButtonText = "重新认证", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            ClearNotice(); ++captureGeneration; configuration.AwaitLogin(); portalCapture = new(captureSession);
            logoutApproved = true; logoutAttempted = false;
            portalOnlineAccount = await ReadPortalAccountAsync();
            if (portalOnlineAccount is null) PortalWeb.CoreWebView2.Navigate(Protocol.Portal);
        }
        finally { SetBusy(false); }
        await PrepareAuthenticationAsync();
    }

    private void HandlePortalMessage(CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!configurationActive || portalCapture is null || closed || configuration.Stage == ConfigurationStage.Saving) return;
        var update = portalCapture.Accept(args.Source, args.WebMessageAsJson);
        if (update == CaptureUpdate.None) return;
        ++captureGeneration; configuration.Receive(portalCapture.Profile!);
        if (update == CaptureUpdate.Response) configuration.Authentication(portalCapture.Authentication!);
        ClearNotice(); UpdateConfiguration();
    }

    private async void RefreshPortal_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        ClearNotice();
        if (configurationActive && candidate is null) { configuration.AwaitLogin(); logoutAttempted = false; }
        if (portalReady) PortalWeb.CoreWebView2.Reload(); else await EnsurePortalAsync();
        UpdateConfiguration();
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (busy || (configurationActive ? !configuration.CanValidate : snapshot?.HasProfile != true)) return;
        bool validatingSession = configurationActive;
        var validating = candidate;
        if (validatingSession && !configuration.Validate()) return;
        SetBusy(true); ClearNotice();
        try
        {
            var reply = await SendAsync(new Command { Name = validatingSession ? "validateCandidate" : "validate", Profile = validating }, false);
            if (validatingSession)
            {
                if (!ReferenceEquals(candidate, validating)) return;
                if (reply?.Authentication is { } auth) configuration.Authentication(auth);
                else configuration.Fail(reply?.Message ?? "未收到验证结果，请重试测试本次配置。");
            }
            else if (reply?.Authentication is { } auth)
                Notice(auth.Success ? "当前保存配置测试成功。" : auth.State == "alreadyOnline" ? "当前账号已在线，配置保持不变；断网恢复仍需实际验证。" : auth.Message, !auth.Success && auth.State != "alreadyOnline");
            else Notice(reply?.Message ?? "未收到当前配置的测试结果。", true);
        }
        finally { SetBusy(false); }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !configuration.Save()) return;
        var saving = candidate; SetBusy(true); ClearNotice();
        try
        {
            var reply = await SendAsync(new Command { Name = "saveProfile", Profile = saving }, false);
            if (reply?.Ok == true && ReferenceEquals(candidate, saving))
            {
                FinishConfiguration(); await SetLoginPageAsync(false);
                Notice("配置已完成。自动重连已启用，关闭窗口后后台仍会继续。");
            }
            else if (ReferenceEquals(candidate, saving)) configuration.Fail(reply?.Message ?? "保存未得到后台确认，请重试保存。");
        }
        finally { SetBusy(false); await RefreshBusinessAsync(); }
    }

    private void FinishConfiguration()
    {
        ++captureGeneration; configuration.End();
        portalCapture = null; portalOnlineAccount = null; logoutApproved = logoutAttempted = false;
        if (snapshot?.HasProfile != true) configurationExpanded = false;
    }
}
