using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NetMaster;

public sealed partial class MainWindow
{
    private string? dismissedNotice;

    private void Notice(string message, bool error = false, string? title = null)
    {
        title ??= error ? "操作未完成" : "状态提示";
        if (dismissedNotice == title + "\n" + message) return;
        if (StatusNotice.Visibility == Visibility.Visible && NoticeTitle.Text == title && NoticeMessage.Text == message) return;
        NoticeTitle.Text = title;
        NoticeMessage.Text = message;
        // The full message remains available in the native Flyout and tooltip.
        int end = message.IndexOfAny(new[] { '。', '\r', '\n' });
        NoticeSummary.Text = end > 0 ? message[..end] : message;
        NoticeIcon.Symbol = error ? Symbol.Important : Symbol.Message;
        NoticeIcon.Foreground = (Brush)Application.Current.Resources[error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush"];
        AutomationProperties.SetName(StatusNotice, title + "：" + message + "。查看详情");
        AutomationProperties.SetLiveSetting(StatusNotice, error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        ToolTipService.SetToolTip(StatusNotice, message);
        StatusNotice.Visibility = Visibility.Visible;
    }

    private void ClearNotice()
    {
        if (StatusNotice is null) return;
        dismissedNotice = null;
        NoticeDetails.Hide();
        StatusNotice.Visibility = Visibility.Collapsed;
    }

    private void DismissNotice_Click(object sender, RoutedEventArgs e)
    {
        dismissedNotice = NoticeTitle.Text + "\n" + NoticeMessage.Text;
        NoticeDetails.Hide();
        StatusNotice.Visibility = Visibility.Collapsed;
    }
}
