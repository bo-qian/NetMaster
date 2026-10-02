using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetMaster;

public enum StatusTone { Neutral, Success, Attention, Working, Caution, Critical }

public sealed partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusIndicator), new PropertyMetadata(""));
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(StatusIndicator), new PropertyMetadata(20d));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(StatusTone), typeof(StatusIndicator), new PropertyMetadata(StatusTone.Neutral, ToneChanged));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public StatusTone Tone { get => (StatusTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public StatusIndicator()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyTone();
    }
    private static void ToneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((StatusIndicator)sender).ApplyTone();
    private void ApplyTone() => VisualStateManager.GoToState(this, Tone.ToString(), false);
}
