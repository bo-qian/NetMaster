using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly ConfigurationSession configuration = new();
    private bool configurationActive => configuration.Active;
    private bool configurationExpanded, preparingAuthentication, logoutApproved, logoutAttempted;
    private bool ConfigurationDetailsVisible => configurationActive || snapshot?.HasProfile == true;
    private PortalCapture? portalCapture;
    private string captureSession = "", captureScriptId = "";
    private string? portalOnlineAccount;
    private readonly List<CoreWebView2Frame> portalFrames = new();
    private LoginProfile? candidate => configuration.Candidate;
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
            if (configurationActive && portalReady && candidate is null)
                try { portalOnlineAccount = await ReadPortalAccountAsync(); await PrepareAuthenticationAsync(); } catch { }
            var reply = await SendAsync(new Command(), false);
            if (backgroundRemoved) return;
            if (reply?.Ok == true)
            {
                if (configurationActive || speedCancellation is not null) await SendAsync(new Command { Name = "manual", Enabled = true }, false);
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
            GuardStatus.Text = backgroundRemoved ? "已停止守护" : value.Guardian;
            ReconnectToggle.IsOn = value.Settings.AutoReconnect; ReconnectToggle.IsEnabled = value.HasProfile && !busy;
            IntervalLabel.Text = $"检测间隔：{value.Settings.IntervalSeconds} 秒";
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
        var configuring = configurationActive;
        if (backgroundRemoved && !configuring && speedCancellation is null) return;
        await SendAsync(new Command { Name = "manual", Enabled = configuring || speedCancellation is not null }, false);
        if (enabled && configuring) await EnsurePortalAsync();
    }
    private async Task EnsurePortalAsync()
    {
        if (portalReady || portalLoading || closed) return;
        portalLoading = true;
        try
        {
            PortalStatus.Text = "正在加载校园网认证网页…";
            var options = new CoreWebView2EnvironmentOptions { ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay };
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(localStorage.Home, "webview"), options);
            await PortalWeb.EnsureCoreWebView2Async(environment);
            UpdatePortalClip();
            var core = PortalWeb.CoreWebView2;
            core.WebMessageReceived += Portal_MessageReceived;
            core.FrameCreated += (_, args) => RegisterPortalFrame(args.Frame);
            core.NavigationCompleted += async (_, args) => { UpdatePortalClip(); await PortalNavigatedAsync(args.IsSuccess); };
            core.NewWindowRequested += (_, args) => { args.Handled = true; if (configurationActive) configuration.Fail("网页请求打开新窗口，请使用外部浏览器入口检查页面。"); UpdateConfiguration(); };
            using (var layoutStream = typeof(MainWindow).Assembly.GetManifestResourceStream("NetMaster.WinUI.PortalLayout.js") ?? throw new IOException("缺少网页适配脚本。"))
            using (var layoutReader = new StreamReader(layoutStream))
                await core.AddScriptToExecuteOnDocumentCreatedAsync(await layoutReader.ReadToEndAsync());
            await InstallCaptureScriptAsync();
            portalReady = true; core.Navigate(Protocol.Portal);
        }
        catch { if (configurationActive) configuration.Fail("内嵌网页初始化失败，请检查 WebView2 Runtime 或点击刷新重试。"); UpdateConfiguration(); }
        finally { portalLoading = false; }
    }
    private async Task InstallCaptureScriptAsync()
    {
        var core = PortalWeb.CoreWebView2;
        if (captureScriptId.Length > 0) core.RemoveScriptToExecuteOnDocumentCreated(captureScriptId);
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("NetMaster.WinUI.PortalCapture.js") ?? throw new IOException("缺少认证捕获脚本。");
        using var reader = new StreamReader(stream);
        captureScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync((await reader.ReadToEndAsync()).Replace("__NETMASTER_SESSION__", captureSession));
    }
    private async Task<string?> ReadPortalAccountAsync()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("NetMaster.WinUI.PortalAccount.js") ?? throw new IOException();
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var account = JsonSerializer.Deserialize<string>(await PortalWeb.CoreWebView2.ExecuteScriptAsync(script));
        if (!string.IsNullOrWhiteSpace(account)) return account;
        // The school's top-level success page embeds its online UI in a frame.
        // Execute inside that frame, with the script checking the school origin.
        foreach (var frame in portalFrames.ToArray())
        {
            try
            {
                account = JsonSerializer.Deserialize<string>(await frame.ExecuteScriptAsync(script));
                if (!string.IsNullOrWhiteSpace(account)) return account;
            }
            catch { /* A frame may be destroyed during navigation. */ }
        }
        return null;
    }
    private void RegisterPortalFrame(CoreWebView2Frame frame)
    {
        portalFrames.Add(frame);
        frame.WebMessageReceived += (_, message) => HandlePortalMessage(message);
        frame.FrameCreated += (_, child) => RegisterPortalFrame(child.Frame);
        frame.Destroyed += (_, _) => portalFrames.Remove(frame);
    }
    private void Portal_MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        => HandlePortalMessage(args);
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
        LogExport.IsEnabled = query.Count > 0;
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
    private void LogSearch_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (StatusNotice is not null) ClearNotice(); FilterLogs(); }
    private void LogFilter_Changed(object sender, SelectionChangedEventArgs args) { if (StatusNotice is not null) ClearNotice(); FilterLogs(); }
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
}
