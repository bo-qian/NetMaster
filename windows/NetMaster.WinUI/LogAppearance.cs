using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Text;

namespace NetMaster.WinUI;

public sealed class LogToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value?.ToString() switch
    {
        "错误" => StatusTone.Critical,
        "警告" => StatusTone.Caution,
        "信息" => StatusTone.Attention,
        _ => StatusTone.Neutral
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
public sealed class LogWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value?.ToString() is "警告" or "错误" ? FontWeights.SemiBold : FontWeights.Normal;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
