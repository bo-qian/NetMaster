using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetMaster.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NetMaster.WinUI;

public sealed partial class MainWindow
{
    private async Task ShowBusinessSettingsAsync()
    {
        ClearNotice();
        var settings = snapshot?.Settings ?? localStorage.Settings;
        string? pendingLocation = null;
        var content = new StackPanel { Spacing = 14 };
        var theme = new ComboBox { Header = "应用主题", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var text in new[] { "跟随系统", "浅色", "深色" }) theme.Items.Add(text);
        theme.SelectedIndex = settings.Theme;
        var interval = new NumberBox { Header = "检测间隔（秒，5–3600）", Value = settings.IntervalSeconds, Minimum = 5, Maximum = 3600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var days = new NumberBox { Header = "日志保留天数（1–365）", Value = settings.RetentionDays, Minimum = 1, Maximum = 365, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var path = new TextBlock { Text = snapshot?.DataDirectory ?? localStorage.DataDirectory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var choose = new Button { Content = "选择保存位置" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var clearProfile = new Button { Content = "清除登录信息", IsEnabled = snapshot?.HasProfile == true || configurationActive };
        var clearLogs = new Button { Content = "清除历史日志" };
        var remove = new Button { Content = "停止后台并关闭登录启动" };
        foreach (var child in new FrameworkElement[] { theme, interval, days,
            new TextBlock { Text = "保存位置（点击保存后生效）" }, path, choose,
            new TextBlock { Text = "独立操作 · 确认后立即生效，取消设置不会撤销", TextWrapping = TextWrapping.Wrap },
            clearProfile, clearLogs, remove, status,
            new TextBlock { Text = "关于 NetMaster\n校园网连接管理 · WinUI 版\n关闭主窗口可保留后台守护；登录后启动由概览中的独立开关控制。卸载请使用 Windows 应用设置。", TextWrapping = TextWrapping.Wrap } }) content.Children.Add(child);
        var dialog = new ContentDialog { Title = "设置", Content = new ScrollViewer { Content = content, MaxHeight = 520 }, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme };
        theme.SelectionChanged += (_, _) => { ApplyTheme(theme.SelectedIndex); dialog.RequestedTheme = Root.RequestedTheme; };
        choose.Click += async (_, _) =>
        {
            choose.IsEnabled = false;
            try
            {
                var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
                pendingLocation = folder.Path; path.Text = System.IO.Path.Combine(folder.Path, "NetMaster-WinUI");
                status.Text = "新位置尚未生效。点击保存提交，取消可放弃。";
            }
            catch { status.Text = "无法选择保存位置。"; }
            finally { choose.IsEnabled = true; }
        };
        ConfirmAction(clearProfile, "清除登录信息并暂停重连，不保留备份。正在进行的配置也会取消。", async () =>
        {
            var reply = await SendAsync(new Command { Name = "clearProfile" }, false);
            status.Text = reply?.Message ?? "未收到后台确认，请检查状态后重试。";
            if (reply?.Ok == true)
            {
                FinishConfiguration(); configurationExpanded = false;
                if (portalReady) PortalWeb.CoreWebView2.Stop();
                await SetLoginPageAsync(false); UpdateConfiguration(); clearProfile.IsEnabled = false;
            }
        });
        ConfirmAction(clearLogs, "清除历史日志，登录配置和守护开关保持。", async () =>
        {
            var reply = await SendAsync(new Command { Name = "clearLogs" }, false);
            status.Text = reply?.Message ?? "未收到后台确认，请重试。";
            if (reply?.Ok == true) await LoadLogsAsync(true);
        });
        ConfirmAction(remove, "停止后台并关闭登录后启动，保留已保存配置和日志。未保存的设置和配置草稿将放弃。", async () =>
        {
            await SetStartupAsync(false);
            applyingState = true;
            try { StartupToggle.IsOn = false; } finally { applyingState = false; }
            var reply = await SendAsync(new Command { Name = "shutdown" }, false);
            status.Text = reply?.Message ?? "后台停止未得到确认，请检查后重试。";
            if (reply?.Ok == true)
            {
                backgroundRemoved = true; FinishConfiguration(); UpdateConfiguration();
                GuardStatus.Text = "已停止守护"; dialog.Hide();
            }
        });
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!double.IsFinite(interval.Value) || !double.IsFinite(days.Value) || interval.Value != Math.Truncate(interval.Value) || days.Value != Math.Truncate(days.Value)) { args.Cancel = true; status.Text = "请输入有效的整数。"; return; }
                var current = snapshot?.Settings ?? settings;
                var updated = current with { Theme = theme.SelectedIndex, IntervalSeconds = (int)interval.Value, RetentionDays = (int)days.Value }; updated.Validate();
                var reply = await SendAsync(new Command { Name = "settings", Settings = updated, DataDirectory = pendingLocation }, false);
                args.Cancel = reply?.Ok != true;
                if (args.Cancel) status.Text = reply?.Message ?? "设置未得到后台确认，请重试。";
                else Notice("设置已保存。");
            }
            catch { args.Cancel = true; status.Text = "设置未保存，请检查输入、保存位置或后台状态。"; }
            finally { deferral.Complete(); }
        };
        void ConfirmAction(Button owner, string message, Func<Task> action)
        {
            var flyoutContent = new StackPanel { Spacing = 12, MaxWidth = 320 };
            flyoutContent.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var confirm = new Button { Content = "确认执行" }; var cancel = new Button { Content = "取消" };
            buttons.Children.Add(confirm); buttons.Children.Add(cancel); flyoutContent.Children.Add(buttons);
            var flyout = new Flyout { Content = flyoutContent }; owner.Flyout = flyout;
            cancel.Click += (_, _) => flyout.Hide();
            confirm.Click += async (_, _) =>
            {
                flyout.Hide(); SetBusy(true);
                dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
                clearProfile.IsEnabled = clearLogs.IsEnabled = remove.IsEnabled = choose.IsEnabled = false;
                try { await action(); }
                catch { status.Text = "操作未完成，请检查当前状态后重试。"; }
                finally
                {
                    SetBusy(false); dialog.IsPrimaryButtonEnabled = true; dialog.IsSecondaryButtonEnabled = true;
                    if (backgroundRemoved) GuardStatus.Text = "已停止守护";
                    clearProfile.IsEnabled = snapshot?.HasProfile == true || configurationActive;
                    clearLogs.IsEnabled = remove.IsEnabled = choose.IsEnabled = true;
                }
            };
        }
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) ApplyTheme(snapshot?.Settings.Theme ?? settings.Theme);
        await RefreshBusinessAsync();
    }
}
