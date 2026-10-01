using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using Windows.System;

namespace NetMaster.WinUI;

public sealed partial class MainWindow : Window
{
    private bool settingsOpen;
    private bool? lastWideWindow;
    private bool portalClipRootObserved;

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

    private void PortalViewport_Loaded(object sender, RoutedEventArgs e)
    {
        if (!portalClipRootObserved && PortalViewport.XamlRoot is { } xamlRoot)
        {
            portalClipRootObserved = true;
            xamlRoot.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdatePortalClip);
        }
        UpdatePortalClip();
    }
    private void PortalViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePortalClip();
        // WebView2 updates its bridge scale/size in its own layout handler.
        DispatcherQueue.TryEnqueue(UpdatePortalClip);
    }
    private void UpdatePortalClip()
    {
        if (PortalViewport.ActualWidth <= 0 || PortalViewport.ActualHeight <= 0) return;
        // WebView2 installs a separate browser placement visual after native
        // initialization. Clip that surface as well as the XAML container.
        ApplyClip(ElementCompositionPreview.GetElementVisual(PortalViewport));
        ApplyClip(ElementCompositionPreview.GetElementVisual(PortalWeb));
        var browserVisual = ElementCompositionPreview.GetElementChildVisual(PortalWeb);
        if (browserVisual is not null) ApplyClip(browserVisual, browserSurface: true);

        void ApplyClip(Visual visual, bool browserSurface = false)
        {
            var clip = visual.Clip as RectangleClip ?? visual.Compositor.CreateRectangleClip();
            var scale = browserSurface ? new Vector2(visual.Scale.X, visual.Scale.Y) : Vector2.One;
            if (scale.X <= 0 || scale.Y <= 0) return;
            clip.Left = clip.Top = 0;
            clip.Right = (float)PortalViewport.ActualWidth / scale.X;
            clip.Bottom = (float)PortalViewport.ActualHeight / scale.Y;
            var radius = new Vector2((float)PortalViewport.CornerRadius.TopLeft) / scale;
            clip.TopLeftRadius = clip.TopRightRadius = clip.BottomLeftRadius = clip.BottomRightRadius = radius;
            visual.Clip = clip;
        }
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
        ClearNotice();
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
        if (PageLayout is null || StatusCards is null || ConfigurationHeader is null || ConfigurationCard is null || LogDetailsCard is null) return;
        bool narrow = ContentViewport.ActualWidth < 760;
        // A zero-width second column still reserves ColumnSpacing in a Grid.
        StatusCards.ColumnSpacing = narrow ? 0 : 18;
        StatusCards.RowSpacing = narrow ? 16 : 0;
        StatusCards.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 3, GridUnitType.Star);
        StatusCards.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        StatusCards.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(GuardCard, narrow ? 0 : 1);
        Grid.SetRow(GuardCard, narrow ? 1 : 0);
        bool stackedConfiguration = ConfigurationHeader.ActualWidth < 740;
        bool splitConfiguration = ConfigurationDetailsVisible && !stackedConfiguration;
        ConfigurationHeader.ColumnSpacing = splitConfiguration ? 18 : 0;
        ConfigurationHeader.RowSpacing = ConfigurationDetailsVisible && stackedConfiguration ? 16 : 0;
        ConfigurationHeader.ColumnDefinitions[0].Width = new GridLength(splitConfiguration ? 3 : 1, GridUnitType.Star);
        ConfigurationHeader.ColumnDefinitions[1].Width = splitConfiguration ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        ConfigurationHeader.RowDefinitions[1].Height = ConfigurationDetailsVisible && stackedConfiguration ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(LoginInfoCard, splitConfiguration ? 1 : 0);
        Grid.SetRow(LoginInfoCard, stackedConfiguration ? 1 : 0);
        bool wrappedConfigurationButtons = ConfigurationCard.ActualWidth < 470;
        ConfigurationButtonRow.RowSpacing = ConfigurationDetailsVisible && wrappedConfigurationButtons ? 12 : 0;
        ConfigurationButtonRow.RowDefinitions[1].Height = ConfigurationDetailsVisible && wrappedConfigurationButtons ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(ConfigurationActions, wrappedConfigurationButtons ? 0 : 2);
        Grid.SetRow(ConfigurationActions, wrappedConfigurationButtons ? 1 : 0);
        Grid.SetColumnSpan(ConfigurationActions, wrappedConfigurationButtons ? 4 : 2);
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
        PageLayout.MinHeight = ContentViewport.ActualHeight;
        UpdatePageHeight();
    }

    private void UpdatePageHeight()
    {
        // Bound the page only when its real text/buttons and useful minimum
        // content areas fit. Smaller windows retain the outer scroll range.
        double chrome = PageLayout.Padding.Top + PageLayout.Padding.Bottom
            + PageHeader.DesiredSize.Height + PageLayout.RowSpacing;
        double minimum = double.PositiveInfinity;
        if (ConfigurationPanel.Visibility == Visibility.Visible)
        {
            minimum = ConfigurationHeader.DesiredSize.Height;
            if (LoginPanel.Visibility == Visibility.Visible)
                minimum += ConfigurationPanel.RowSpacing + Insets(PortalCard)
                    + PortalHeading.DesiredSize.Height + PortalAddressRow.DesiredSize.Height
                    + PortalStatus.DesiredSize.Height + 3 * PortalLayout.RowSpacing + PortalWeb.MinHeight;
        }
        else if (LogsPanel.Visibility == Visibility.Visible)
            minimum = LogFiltersCard.DesiredSize.Height + 2 * LogsPanel.RowSpacing + LogDetailsCard.Height
                + Insets(LogRecordsCard) + LogColumns.DesiredSize.Height + LogFooter.DesiredSize.Height
                + 2 * LogRecordsLayout.RowSpacing + 120;
        bool fits = ContentViewport.ActualHeight >= chrome + minimum;
        PageLayout.Height = fits ? ContentViewport.ActualHeight : double.NaN;
        LogList.MaxHeight = fits ? double.PositiveInfinity : 320;

        static double Insets(Border card) => card.Padding.Top + card.Padding.Bottom
            + card.BorderThickness.Top + card.BorderThickness.Bottom;
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
