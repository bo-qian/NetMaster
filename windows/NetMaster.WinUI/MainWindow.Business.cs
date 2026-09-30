using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using NetMaster.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NetMaster.WinUI;

public sealed partial class MainWindow
{
    private readonly AppStorage localStorage = new();
    private WorkerClient? worker, statusWorker;
    private Snapshot? snapshot;
    private readonly CancellationTokenSource windowLifetime = new();
    private CancellationTokenSource? speedCancellation;
    private DispatcherTimer? businessTimer;
    private bool businessReady, refreshing, applyingState, busy, closed, portalReady, portalLoading, backgroundRemoved, changingStartup;
    private long captureGeneration;
    private bool configurationActive;
    private LoginProfile? candidate;
    private IReadOnlyList<LogEntry> allLogs = Array.Empty<LogEntry>();
    private readonly LogReader logReader = new();
    private readonly ObservableCollection<LogEntry> displayedLogs = new();
    private int logLimit = 100;
    private string WorkerExecutable => Path.Combine(AppContext.BaseDirectory, "NetMaster.Worker.exe");
    private bool Packaged { get { try { _ = Package.Current.Id; return true; } catch { return false; } } }

    private async void Business_Loaded(object sender, RoutedEventArgs e)
    {
        if (businessReady) return;
        businessReady = true;
        worker = new WorkerClient(localStorage.Home, WorkerExecutable);
        statusWorker = new WorkerClient(localStorage.Home, WorkerExecutable);
        LogList.ItemsSource = displayedLogs;
        ApplyTheme(localStorage.Settings.Theme);
        businessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        businessTimer.Tick += async (_, _) => await RefreshBusinessAsync();
        businessTimer.Start();
        await RefreshBusinessAsync();
        if (ConfigurationPanel.Visibility == Visibility.Visible) await SetLoginPageAsync(true);
    }
    private void Business_Closed(object sender, WindowEventArgs e)
    {
        closed = true; businessTimer?.Stop(); windowLifetime.Cancel(); speedCancellation?.Cancel();
        // Release only the temporary manual-operation lease; do not stop enabled guarding.
        if (worker is not null) _ = ReleaseLeaseAsync();
        try { PortalWeb.Close(); } catch { }
    }
    private async Task ReleaseLeaseAsync() { try { await worker!.SendAsync(new Command { Name = "manual", Enabled = false }, allowLaunch: false); } catch { } }
    private void ApplyTheme(int theme)
    {
        Root.RequestedTheme = (ElementTheme)theme;
        AppWindow.TitleBar.PreferredTheme = theme switch { 1 => TitleBarTheme.Light, 2 => TitleBarTheme.Dark, _ => TitleBarTheme.UseDefaultAppMode };
    }
    private async Task<Reply?> SendAsync(Command command, bool feedback = true)
    {
        if (worker is null || closed) return null;
        try
        {
            var reply = await (command.Name == "status" ? statusWorker! : worker).SendAsync(command, windowLifetime.Token);
            if (closed) return null;
            if (command.Name == "status" && backgroundRemoved) return null;
            if (reply.Ok && command.Name is not ("shutdown" or "status")) backgroundRemoved = false;
            if (reply.Snapshot is not null) ApplySnapshot(reply.Snapshot);
            if (feedback && (!reply.Ok || reply.Message.Length > 0)) Notice(reply.Message, !reply.Ok);
            return reply;
        }
        catch (OperationCanceledException) when (closed) { return null; }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException)
        {
            if (!closed) { GuardStatus.Text = "后台未连接"; NetworkStatus.Text = "状态待重新确认"; Notice("未收到后台确认，请检查安装或稍后重试。当前操作是否生效需重新读取状态。", true); }
            return null;
        }
    }
    private void Notice(string message, bool error = false)
    {
        StatusNotice.Title = error ? "操作未完成" : "状态提示";
        StatusNotice.Message = message; StatusNotice.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational; StatusNotice.IsOpen = true;
    }
    private async Task RefreshBusinessAsync()
    {
        if (refreshing || busy || closed || backgroundRemoved || worker is null) return;
        refreshing = true;
        try
        {
            var reply = await SendAsync(new Command(), false);
            if (backgroundRemoved) return;
            if (reply?.Ok == true)
            {
                if (configurationActive && ConfigurationPanel.Visibility == Visibility.Visible || speedCancellation is not null) await SendAsync(new Command { Name = "manual", Enabled = true }, false);
                await LoadLogsAsync();
                applyingState = true;
                try { if (!changingStartup) { StartupToggle.IsOn = await GetStartupAsync(); StartupToggle.IsEnabled = true; } }
                catch { StartupToggle.IsEnabled = false; }
                finally { applyingState = false; }
            }
        }
        finally { refreshing = false; }
    }
    private void ApplySnapshot(Snapshot value)
    {
        snapshot = value; applyingState = true;
        try
        {
            NetworkStatus.Text = value.Network.State switch { "online" => "已连接互联网", "authentication" => "需要认证", "offline" => "网络未连接", "uncertain" => "检测未通过", _ => "尚未检测" };
            OverviewAccount.Text = "配置账号：" + value.Account;
            LastCheck.Text = "上次检测：" + (value.Network.State == "unknown" ? "暂无" : value.Network.CheckedAt.ToLocalTime().ToString("HH:mm:ss"));
            GuardStatus.Text = value.Guardian;
            ReconnectToggle.IsOn = value.Settings.AutoReconnect; ReconnectToggle.IsEnabled = value.HasProfile && !busy;
            IntervalLabel.Text = $"检测间隔：{value.Settings.IntervalSeconds} 秒";
            LoginSaved.Text = value.HasProfile ? "登录信息：已加密保存" : "登录信息：未保存";
            if (candidate is null) { LoginAccount.Text = "配置账号：" + value.Account; LoginState.Text = value.HasProfile ? "已保存登录信息" : "等待登录"; }
            ValidateButton.IsEnabled = !busy && (candidate is not null || value.HasProfile);
            SaveProfileButton.IsEnabled = !busy && candidate?.Confirmed == true;
            UpdateConfiguration();
            if (value.StorageError is not null) Notice(value.StorageError, true);
            else if (StatusNotice.Title.ToString() == "正在启动") { StatusNotice.IsOpen = false; }
        }
        finally { applyingState = false; }
    }
    private void SetBusy(bool value)
    {
        busy = value; DetectButton.IsEnabled = !value; PortalWeb.IsHitTestVisible = !value;
        if (snapshot is not null) ApplySnapshot(snapshot);
    }
    private async void Detect_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return; SetBusy(true); NetworkStatus.Text = "正在检测";
        try { await SendAsync(new Command { Name = "detect" }); }
        finally { SetBusy(false); }
    }
    private async void Reconnect_Toggled(object sender, RoutedEventArgs e)
    {
        if (!businessReady || applyingState || busy) return;
        var enabled = ReconnectToggle.IsOn; SetBusy(true);
        try { await SendAsync(new Command { Name = "reconnect", Enabled = enabled }); }
        finally { SetBusy(false); await RefreshBusinessAsync(); }
    }
    private async Task<bool> GetStartupAsync()
    {
        if (Packaged) { var task = await StartupTask.GetAsync("NetMasterGuardian"); return task.State == StartupTaskState.Enabled; }
        return await Task.Run(StartupRegistration.Enabled);
    }
    private async Task SetStartupAsync(bool enabled)
    {
        if (Packaged)
        {
            var task = await StartupTask.GetAsync("NetMasterGuardian");
            if (!enabled) { task.Disable(); return; }
            if (await task.RequestEnableAsync() != StartupTaskState.Enabled) throw new IOException("启动项被 Windows 或用户禁用。");
        }
        else
        {
            if (enabled && AppContext.BaseDirectory.Contains("preview", StringComparison.OrdinalIgnoreCase)) throw new IOException("预览输出路径会变化，请从正式发布目录运行后开启登录启动。");
            await Task.Run(() => StartupRegistration.SetEnabled(enabled, WorkerExecutable, localStorage.Home));
        }
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!businessReady || applyingState || busy) return;
        changingStartup = true; StartupToggle.IsEnabled = false;
        try { await SetStartupAsync(StartupToggle.IsOn); Notice(StartupToggle.IsOn ? "Windows 登录后将启动后台；是否自动重连仍由重连开关决定。" : "已关闭登录后启动，当前后台不受影响。"); }
        catch { Notice("登录启动未能更改。请检查 Windows 启动应用设置；预览构建需从正式发布目录运行。", true); }
        finally { applyingState = true; try { StartupToggle.IsOn = await GetStartupAsync(); } catch { } applyingState = false; changingStartup = false; StartupToggle.IsEnabled = true; }
    }
    private async Task SetLoginPageAsync(bool enabled)
    {
        var configuring = enabled && configurationActive;
        if (backgroundRemoved && !configuring && speedCancellation is null) return;
        await SendAsync(new Command { Name = "manual", Enabled = configuring || speedCancellation is not null }, false);
        if (configuring) await EnsurePortalAsync();
    }
    private void UpdateConfiguration()
    {
        if (ConfigurationState is null) return;
        var saved = snapshot?.HasProfile == true;
        ConfigurationState.Text = configurationActive ? candidate?.Confirmed == true ? "登录信息已确认，等待保存" : candidate is not null ? "已获取登录信息，等待确认" : "正在配置" : saved ? "已配置" : "尚未配置";
        ConfigurationAccount.Text = saved ? "当前配置账号：" + snapshot!.Account : "配置一次，之后由 NetMaster 自动检测并恢复校园网连接。";
        ConfigurationSteps.Text = configurationActive ? candidate?.Confirmed == true ? "① 已获取　→　② 已确认　→　③ 保存并启用守护" : candidate is not null ? "① 已获取　→　② 验证登录　→　③ 保存并启用守护" : "① 获取登录信息　→　② 确认有效　→　③ 保存并启用守护" : "① 获取登录信息　→　② 确认有效　→　③ 保存并启用守护";
        ConfigurationMessage.Text = configurationActive ? candidate?.Confirmed == true ? "登录信息已确认。点击下方保存按钮，完成配置并启用守护。" : snapshot?.Network.State == "online" && candidate is null ? "当前已联网。点击重新认证后填写登录信息，软件会继续引导配置；重新认证会暂时断网。" : "请在下方完成认证。配置期间暂缓自动重连，新信息保存成功后才替换原配置。" : saved ? snapshot!.Settings.AutoReconnect ? "后台守护已启用。需要更换账号或登录信息时，点击重新配置。" : "登录信息已保存，自动重连已暂停。可在概览恢复守护，也可以重新配置。" : "点击开始配置，软件会引导获取登录信息、确认并保存。";
        StartConfigurationButton.Content = saved ? "重新配置" : "开始配置";
        OverviewConfigureButton.Content = saved ? "重新配置" : "开始配置";
        StartConfigurationButton.Visibility = configurationActive ? Visibility.Collapsed : Visibility.Visible;
        StartConfigurationButton.IsEnabled = !busy && businessReady;
        CancelConfigurationButton.Visibility = configurationActive ? Visibility.Visible : Visibility.Collapsed;
        CancelConfigurationButton.IsEnabled = !busy;
        RestartAuthenticationButton.Visibility = configurationActive && candidate?.Confirmed != true ? Visibility.Visible : Visibility.Collapsed;
        RestartAuthenticationButton.IsEnabled = !busy && portalReady;
        LoginPanel.Visibility = configurationActive ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void StartConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !businessReady || configurationActive) return;
        SetBusy(true);
        try
        {
            var reply = await SendAsync(new Command { Name = "manual", Enabled = true });
            if (reply?.Ok != true) return;
            ++captureGeneration; configurationActive = true; UpdateConfiguration();
            LoginExplanation.Text = "请完成一次认证。软件会获取本次登录信息，确认后即可保存并启用守护。";
            var wasReady = portalReady;
            await EnsurePortalAsync();
            if (wasReady && portalReady) PortalWeb.CoreWebView2.Navigate(Protocol.Portal);
        }
        finally { SetBusy(false); }
    }
    private async void CancelConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        ++captureGeneration; configurationActive = false; candidate = null;
        if (portalReady) PortalWeb.CoreWebView2.Stop();
        UpdateConfiguration();
        await SetLoginPageAsync(ConfigurationPanel.Visibility == Visibility.Visible);
        Notice("已取消本次配置，原有配置保持不变。");
    }
    private async void RestartAuthentication_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !configurationActive || !portalReady || settingsOpen) return;
        var dialog = new ContentDialog { Title = "重新认证", Content = "重新认证将退出当前校园网连接，网络会暂时断开。随后填写账号和密码，软件将获取新的登录信息。原配置保留到新配置保存成功。", PrimaryButtonText = "重新认证", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
        SetBusy(true);
        try
        {
            if ((await SendAsync(new Command { Name = "manual", Enabled = true }, false))?.Ok != true) return;
            var result = await PortalWeb.CoreWebView2.ExecuteScriptAsync("""
                (() => {
                    if (location.origin !== 'http://10.10.9.9') return 'unsupported';
                    const buttons = Array.from(document.querySelectorAll('button,a,input[type="button"],input[type="submit"]'))
                        .filter(node => !node.disabled && node.getClientRects().length > 0 &&
                            /^(注销|退出|退出登录|下线)$/.test((node.innerText || node.value || node.textContent || '').replace(/\s/g, '')));
                    if (buttons.length !== 1) return 'unavailable';
                    buttons[0].click();
                    return 'requested';
                })()
                """);
            if (JsonSerializer.Deserialize<string>(result) == "requested")
            {
                ++captureGeneration; candidate = null;
                PortalStatus.Text = "已请求重新认证，请等待网页显示登录表单后填写账号和密码。";
                LoginExplanation.Text = "重新认证成功后，软件会获取新的登录信息。";
            }
            else PortalStatus.Text = "当前网页未提供可识别的重新认证入口。若已显示登录表单，直接填写；若显示已登录，可使用网页中的注销入口。";
        }
        catch { PortalStatus.Text = "重新认证请求未完成，请查看网页状态后重试。"; }
        finally { SetBusy(false); }
    }
    private async Task EnsurePortalAsync()
    {
        if (portalReady || portalLoading || closed) return;
        portalLoading = true;
        try
        {
            PortalStatus.Text = "正在加载校园网认证网页…";
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(localStorage.Home, "webview"), null);
            await PortalWeb.EnsureCoreWebView2Async(environment);
            var core = PortalWeb.CoreWebView2;
            core.WebResourceResponseReceived += Portal_ResponseReceived;
            core.NavigationCompleted += (_, args) => PortalStatus.Text = args.IsSuccess ? "请在网页登录。成功认证后可保存并启用守护。" : "校园网网页无法加载，请确认网络连接，或使用浏览器打开后重试。";
            core.NewWindowRequested += (_, args) => { args.Handled = true; PortalStatus.Text = "认证网页请求打开新窗口，请使用外部浏览器入口。"; };
            portalReady = true; core.Navigate(Protocol.Portal);
        }
        catch { PortalStatus.Text = "内嵌网页初始化失败，请检查 WebView2 Runtime 安装，或使用外部浏览器入口。"; }
        finally { portalLoading = false; }
    }
    private async void Portal_ResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (!configurationActive || ConfigurationPanel.Visibility != Visibility.Visible || !Protocol.IsLogin(args.Request.Uri, args.Request.Method) || closed) return;
        var generation = ++captureGeneration;
        try
        {
            if (args.Request.Content is null) { PortalStatus.Text = "未能读取认证信息，请在网页重新提交登录。"; return; }
            using var requestStream = args.Request.Content.AsStreamForRead();
            if (requestStream.CanSeek) requestStream.Position = 0;
            using var requestContent = new System.Net.Http.StreamContent(requestStream);
            var payload = await NetworkService.ReadBoundedAsync(requestContent, 65536, windowLifetime.Token);
            var profile = new LoginProfile { Payload = payload }; profile.Validate();
            var responseContent = await args.Response.GetContentAsync();
            if (responseContent is null) return;
            using var responseStream = responseContent.AsStreamForRead();
            using var content = new System.Net.Http.StreamContent(responseStream);
            var result = NetworkService.ParseAuthentication(await NetworkService.ReadBoundedAsync(content, 131072, windowLifetime.Token));
            if (closed || generation != captureGeneration) return;
            // Keep the latest observed response if asynchronous reads finish out of order.
            candidate = profile with { Confirmed = result.Success, VerifiedAt = result.Success ? DateTimeOffset.Now : null };
            LoginAccount.Text = "配置账号：" + Protocol.Mask(candidate.Account);
            LoginState.Text = result.Success ? "认证成功，尚未保存" : "已获取，待验证";
            LoginExplanation.Text = result.Message;
            if (snapshot is not null) ApplySnapshot(snapshot);
            await SendAsync(new Command { Name = "detect" }, false);
        }
        catch (OperationCanceledException) when (closed) { }
        catch { if (!closed) PortalStatus.Text = "未获取到可用的认证信息，请重新登录或重试验证。"; }
    }
    private async void RefreshPortal_Click(object sender, RoutedEventArgs e) { if (portalReady) PortalWeb.CoreWebView2.Reload(); else await EnsurePortalAsync(); }
    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return; var validating = candidate; SetBusy(true);
        try
        {
            var result = await SendAsync(new Command { Name = "validate", Profile = validating });
            if (ReferenceEquals(candidate, validating) && result?.Authentication is { } auth)
            {
                LoginExplanation.Text = auth.Message;
                if (candidate is not null && auth.Success) { candidate = candidate with { Confirmed = true, VerifiedAt = DateTimeOffset.Now }; LoginState.Text = "验证成功，尚未保存"; }
                else if (candidate is not null && auth.State == "rejected") candidate = candidate with { Confirmed = false };
            }
        }
        finally { SetBusy(false); }
    }
    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (busy || candidate?.Confirmed != true) return; var saving = candidate; SetBusy(true);
        try { var reply = await SendAsync(new Command { Name = "saveProfile", Profile = saving }); if (reply?.Ok == true && ReferenceEquals(candidate, saving)) { ++captureGeneration; candidate = null; configurationActive = false; LoginExplanation.Text = "配置已完成，后台守护已启用。"; UpdateConfiguration(); await SetLoginPageAsync(ConfigurationPanel.Visibility == Visibility.Visible); Notice("配置已完成，后台守护已启用。关闭主窗口后仍会继续。"); } }
        finally { SetBusy(false); await RefreshBusinessAsync(); }
    }
    private async void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (speedCancellation is not null) { speedCancellation.Cancel(); return; }
        if (busy) return;
        speedCancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        SpeedButton.Content = "取消测速";
        try
        {
            await SendAsync(new Command { Name = "manual", Enabled = true }, false);
            var result = await new SpeedService().RunAsync(new Progress<string>(text => { if (!closed) SpeedMessage.Text = text; }), speedCancellation.Token);
            if (closed) return;
            DownloadValue.Text = result.DownloadMbps is { } down ? $"{down:F1} Mbps" : "— Mbps";
            UploadValue.Text = result.UploadMbps is { } up ? $"{up:F1} Mbps" : "— Mbps";
            LatencyValue.Text = result.LatencyMs is { } latency ? $"{latency:F0} ms" : "— ms";
            SpeedMessage.Text = result.Message;
        }
        finally { speedCancellation.Dispose(); speedCancellation = null; if (!closed) { SpeedButton.Content = "开始测速"; await SetLoginPageAsync(ConfigurationPanel.Visibility == Visibility.Visible); } }
    }
    private async Task LoadLogsAsync(bool force = false)
    {
        if (snapshot is null || closed) return;
        try
        {
            var directory = snapshot.DataDirectory;
            var entries = await Task.Run(() => logReader.Read(directory), windowLifetime.Token);
            if (closed) return;
            RecentLogs.Text = entries.Count == 0 ? "暂无日志。" : string.Join("\n", entries.Take(2).Select(e => e.Display));
            if (LiveLogs.IsOn || force) { allLogs = entries; FilterLogs(false); }
        }
        catch (OperationCanceledException) when (closed) { }
        catch { if (!closed) Notice("日志读取失败，请检查保存位置。", true); }
    }
    private IEnumerable<LogEntry> FilteredLogs()
    {
        var query = allLogs.AsEnumerable(); string search = LogSearch.Text.Trim();
        if (search.Length > 0) query = query.Where(e => e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Event.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Source.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (LogLevel.SelectedIndex > 0) query = query.Where(e => e.Level == ((ComboBoxItem)LogLevel.SelectedItem).Content.ToString());
        if (LogDate.SelectedIndex > 0) { var since = DateTime.Today.AddDays(LogDate.SelectedIndex == 1 ? 0 : -6); query = query.Where(e => e.Time.LocalDateTime >= since); }
        return query;
    }
    private void FilterLogs(bool reset = true)
    {
        if (LogList is null || LogDate is null || LogLevel is null || LogSearch is null) return;
        if (reset) logLimit = 100;
        var selected = (LogList.SelectedItem as LogEntry)?.Id;
        var query = FilteredLogs().ToList(); var page = query.Take(logLimit).ToList();
        if (!displayedLogs.Select(e => e.Id).SequenceEqual(page.Select(e => e.Id)))
        {
            var scroll = FindChild<ScrollViewer>(LogList);
            var offset = scroll?.VerticalOffset ?? 0;
            for (int index = 0; index < page.Count; index++)
            {
                if (index < displayedLogs.Count && displayedLogs[index].Id == page[index].Id) continue;
                var existing = displayedLogs.FirstOrDefault(e => e.Id == page[index].Id);
                if (existing is not null) displayedLogs.Move(displayedLogs.IndexOf(existing), index);
                else displayedLogs.Insert(index, page[index]);
            }
            while (displayedLogs.Count > page.Count) displayedLogs.RemoveAt(displayedLogs.Count - 1);
            if (selected is not null) LogList.SelectedItem = displayedLogs.FirstOrDefault(e => e.Id == selected);
            if (AutoScroll.IsChecked == true && displayedLogs.Count > 0) LogList.ScrollIntoView(displayedLogs[0]);
            else if (!reset && scroll is not null) DispatcherQueue.TryEnqueue(() => { if (!closed) scroll.ChangeView(null, offset, null, true); });
        }
        LogsEmpty.Visibility = page.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogCount.Text = $"{page.Count} / {query.Count} 条"; MoreLogs.Visibility = page.Count < query.Count ? Visibility.Visible : Visibility.Collapsed;
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void LogSearch_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => FilterLogs();
    private void LogFilter_Changed(object sender, SelectionChangedEventArgs args) => FilterLogs();
    private async void LiveLogs_Toggled(object sender, RoutedEventArgs e) { if (businessReady && LiveLogs.IsOn) await LoadLogsAsync(true); }
    private void MoreLogs_Click(object sender, RoutedEventArgs e) { logLimit += 100; FilterLogs(false); }
    private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (LogDetails is null) return;
        var selected = LogList.SelectedItem as LogEntry;
        LogDetails.Text = selected?.Details ?? "选择一条记录以查看详细信息。"; CopyLogButton.IsEnabled = selected is not null;
    }
    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogList.SelectedItem is not LogEntry entry) return;
        try { var data = new DataPackage(); data.SetText(entry.Details); Clipboard.SetContent(data); Notice("已复制选中记录。"); }
        catch { Notice("剪贴板暂时不可用，请重试。", true); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"NetMaster-日志-{DateTime.Now:yyyyMMdd-HHmmss}" };
            picker.FileTypeChoices.Add("日志文本", new List<string> { ".txt" }); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            var entries = FilteredLogs().ToList();
            await Windows.Storage.FileIO.WriteTextAsync(file, string.Join("\n\n", entries.Select(entry => entry.Details)));
            Notice($"已导出当前筛选范围，共 {entries.Count} 条记录。");
        }
        catch { Notice("导出失败，请检查目标文件权限。", true); }
    }
    private async Task ShowBusinessSettingsAsync()
    {
        var settings = snapshot?.Settings ?? localStorage.Settings;
        var content = new StackPanel { Spacing = 14 };
        var theme = new ComboBox { Header = "应用主题", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var text in new[] { "跟随系统", "浅色", "深色" }) theme.Items.Add(text); theme.SelectedIndex = settings.Theme;
        theme.SelectionChanged += (_, _) => ApplyTheme(theme.SelectedIndex);
        var interval = new NumberBox { Header = "检测间隔（秒，5–3600）", Value = settings.IntervalSeconds, Minimum = 5, Maximum = 3600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var days = new NumberBox { Header = "日志保留天数（1–365）", Value = settings.RetentionDays, Minimum = 1, Maximum = 365, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var path = new TextBlock { Text = snapshot?.DataDirectory ?? localStorage.DataDirectory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var choose = new Button { Content = "更改保存位置" };
        var import = new Button { Content = "导入旧版登录配置" };
        var remove = new Button { Content = "移除后台启动并停止守护" };
        var confirmClear = new CheckBox { Content = "我确认清除下面选择的数据", IsChecked = false };
        var clearProfile = new Button { Content = "清除已保存的登录信息", IsEnabled = false };
        var clearLogs = new Button { Content = "清除历史日志", IsEnabled = false };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        foreach (var child in new FrameworkElement[] { theme, interval, days, new TextBlock { Text = "数据保存位置" }, path, choose, import, remove, confirmClear, clearProfile, clearLogs, status, new TextBlock { Text = "关于 NetMaster\n校园网连接管理 · WinUI 版\n关闭窗口可保留已启用的后台守护。移除后台保留配置和日志；卸载应用请使用 Windows 应用设置。更改保存位置留下的原目录备份需单独处理。", TextWrapping = TextWrapping.Wrap } }) content.Children.Add(child);
        void UpdateClearButtons() { clearProfile.IsEnabled = clearLogs.IsEnabled = confirmClear.IsChecked == true; }
        confirmClear.Checked += (_, _) => UpdateClearButtons();
        confirmClear.Unchecked += (_, _) => UpdateClearButtons();
        clearProfile.Click += async (_, _) =>
        {
            confirmClear.IsChecked = false;
            var reply = await SendAsync(new Command { Name = "clearProfile" }, false);
            status.Text = reply?.Message ?? "未收到后台确认。";
            if (reply?.Ok == true) { ++captureGeneration; candidate = null; LoginState.Text = "等待登录"; LoginExplanation.Text = "已清除当前保存的登录信息。旧版文件及迁移备份需单独处理。"; if (snapshot is not null) ApplySnapshot(snapshot); }
        };
        clearLogs.Click += async (_, _) =>
        {
            confirmClear.IsChecked = false;
            var reply = await SendAsync(new Command { Name = "clearLogs" }, false);
            status.Text = reply?.Message ?? "未收到后台确认。";
            if (reply?.Ok == true) await LoadLogsAsync(true);
        };
        choose.Click += async (_, _) =>
        {
            choose.IsEnabled = false;
            try
            {
                var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
                var reply = await SendAsync(new Command { Name = "move", DataDirectory = folder.Path }, false);
                status.Text = reply?.Message ?? "未收到后台确认。"; if (reply?.Ok == true) path.Text = reply.Snapshot!.DataDirectory;
            }
            catch { status.Text = "无法更改保存位置。"; }
            finally { choose.IsEnabled = true; }
        };
        import.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync(); if (file is null) return;
                if ((await file.GetBasicPropertiesAsync()).Size > 131072) throw new InvalidDataException();
                using var json = JsonDocument.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));
                var root = json.RootElement;
                if (root.TryGetProperty("login_url", out var url) && !Protocol.IsLogin(url.GetString() ?? "", "POST")) throw new InvalidDataException();
                var p = new LoginProfile { Payload = root.GetProperty("login_payload").GetString() ?? "" }; p.Validate(); ++captureGeneration; candidate = p;
                status.Text = "旧版登录信息已导入内存，请关闭设置后验证登录。未修改旧版任务或配置。";
                LoginAccount.Text = "配置账号：" + Protocol.Mask(p.Account); LoginState.Text = "已导入，待验证"; if (snapshot is not null) ApplySnapshot(snapshot);
            }
            catch { status.Text = "不是可用的旧版校园网配置，未导入。"; }
        };
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false; SetBusy(true);
            try { await SetStartupAsync(false); var reply = await SendAsync(new Command { Name = "shutdown" }, false); status.Text = reply?.Message ?? "未收到后台确认。"; if (reply?.Ok == true) { backgroundRemoved = true; GuardStatus.Text = "已停止守护"; } }
            catch { status.Text = "移除未完成，请检查 Windows 启动项设置。"; }
            finally { SetBusy(false); if (backgroundRemoved) GuardStatus.Text = "已停止守护"; remove.IsEnabled = true; }
        };
        var dialog = new ContentDialog { Title = "设置", Content = new ScrollViewer { Content = content, MaxHeight = 520 }, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = Root.XamlRoot };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!double.IsFinite(interval.Value) || !double.IsFinite(days.Value) || interval.Value != Math.Truncate(interval.Value) || days.Value != Math.Truncate(days.Value)) { args.Cancel = true; status.Text = "请输入有效的整数。"; return; }
                var current = snapshot?.Settings ?? settings;
                var updated = current with { Theme = theme.SelectedIndex, IntervalSeconds = (int)interval.Value, RetentionDays = (int)days.Value }; updated.Validate();
                var reply = await SendAsync(new Command { Name = "settings", Settings = updated }, false);
                args.Cancel = reply?.Ok != true; if (args.Cancel) status.Text = reply?.Message ?? "设置未得到后台确认，请重试。";
            }
            catch { args.Cancel = true; status.Text = "设置未保存，请检查输入或后台状态。"; }
            finally { deferral.Complete(); }
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) ApplyTheme(snapshot?.Settings.Theme ?? settings.Theme);
        await RefreshBusinessAsync();
    }
}
