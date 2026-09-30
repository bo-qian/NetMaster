using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.System;

namespace NetMaster.WinUI;

public sealed partial class MainWindow : Window
{
    private bool settingsOpen;
    private bool? lastWideWindow;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        WindowSizing.Attach(this, 1120, 800, 720, 520);
        Navigation.SelectedItem = Navigation.MenuItems[0];
        Root.Loaded += Business_Loaded;
        Closed += Business_Closed;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || OverviewPanel is null) return;
        string page = item.Tag?.ToString() ?? "overview";
        OverviewPanel.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationPanel.Visibility = page == "login" ? Visibility.Visible : Visibility.Collapsed;
        LogsPanel.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageHeading.Text = item.Content.ToString();
        PageDescription.Text = page switch
        {
            "login" => "完成一次配置，之后自动恢复校园网连接",
            "logs" => "查看网络检测、登录和守护记录",
            _ => "连接状态与最近活动"
        };
        UpdateResponsiveLayout();
        ContentViewport.ChangeView(null, 0, null, true);
        if (businessReady) _ = SetLoginPageAsync(page == "login");
    }

    private void ContentViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Navigation is null) return;
        bool wide = e.NewSize.Width >= 1008;
        // Only change the pane at a width breakpoint; keep manual toggling available.
        if (lastWideWindow != wide)
        {
            lastWideWindow = wide;
            Navigation.IsPaneOpen = wide;
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (PageLayout is null || StatusCards is null || LoginPanel is null) return;
        bool narrow = ContentViewport.ActualWidth < 760;
        // A zero-width second column still reserves ColumnSpacing in a Grid.
        StatusCards.ColumnSpacing = narrow ? 0 : 18;
        LoginPanel.ColumnSpacing = narrow ? 0 : 18;
        StatusCards.RowSpacing = narrow ? 16 : 0;
        LoginPanel.RowSpacing = narrow ? 16 : 0;
        StatusCards.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 3, GridUnitType.Star);
        StatusCards.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        StatusCards.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(GuardCard, narrow ? 0 : 1);
        Grid.SetRow(GuardCard, narrow ? 1 : 0);
        LoginPanel.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 2, GridUnitType.Star);
        LoginPanel.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        LoginPanel.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(LoginInfoCard, narrow ? 0 : 1);
        Grid.SetRow(LoginInfoCard, narrow ? 1 : 0);
        bool compactFilters = ContentViewport.ActualWidth < 640;
        LogFilters.RowSpacing = compactFilters ? 12 : 0;
        LogFilters.RowDefinitions[1].Height = compactFilters ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(LogSearch, compactFilters ? 4 : 1);
        Grid.SetColumn(LogLevel, compactFilters ? 0 : 1);
        Grid.SetRow(LogLevel, compactFilters ? 1 : 0);
        Grid.SetColumn(LogDate, compactFilters ? 1 : 2);
        Grid.SetColumnSpan(LogDate, compactFilters ? 2 : 1);
        Grid.SetRow(LogDate, compactFilters ? 1 : 0);
        Grid.SetRow(LogExport, compactFilters ? 1 : 0);
        LogFooter.RowSpacing = compactFilters ? 12 : 0;
        LogFooter.RowDefinitions[1].Height = compactFilters ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(LogFooterOptions, compactFilters ? 2 : 1);
        Grid.SetRow(CopyLogButton, compactFilters ? 1 : 0);
        bool compactMetrics = ContentViewport.ActualWidth < 560;
        SpeedMetrics.ColumnSpacing = compactMetrics ? 0 : 24;
        SpeedMetrics.RowSpacing = compactMetrics ? 12 : 0;
        for (int i = 1; i < 3; i++)
        {
            SpeedMetrics.ColumnDefinitions[i].Width = compactMetrics ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            SpeedMetrics.RowDefinitions[i].Height = compactMetrics ? GridLength.Auto : new GridLength(0);
        }
        Grid.SetColumn(UploadMetric, compactMetrics ? 0 : 1);
        Grid.SetColumn(LatencyMetric, compactMetrics ? 0 : 2);
        Grid.SetRow(UploadMetric, compactMetrics ? 1 : 0);
        Grid.SetRow(LatencyMetric, compactMetrics ? 2 : 0);
        DownloadMetric.Orientation = UploadMetric.Orientation = LatencyMetric.Orientation = compactMetrics ? Orientation.Horizontal : Orientation.Vertical;
        // Let wrapped content determine the scroll extent, including bottom padding.
        // A fixed Height can clip the last card even at the end of the scroll range.
        PageLayout.MinHeight = ContentViewport.ActualHeight;
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = Navigation.MenuItems[1];
        if (businessReady && !configurationActive) StartConfiguration_Click(sender, e);
    }
    private void Logs_Click(object sender, RoutedEventArgs e) => Navigation.SelectedItem = Navigation.MenuItems[2];

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "settings" })
            Settings_Click(sender, new RoutedEventArgs());
    }

    private async void OpenPortal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool opened = await Launcher.LaunchUriAsync(new Uri("http://10.10.9.9"));
            PortalStatus.Text = opened ? "已请求浏览器打开认证页面；外部登录不会自动获取守护所需的登录信息。" : "无法打开浏览器，请手动访问上方地址。";
        }
        catch
        {
            PortalStatus.Text = "无法打开浏览器，请手动访问上方地址。";
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsOpen) return;
        settingsOpen = true;
        try
        {
            await ShowBusinessSettingsAsync();
        }
        finally { settingsOpen = false; }
    }
}
