using System.Windows;
using System.Windows.Controls;

namespace Pegged.Views;

/// <summary>
/// 使用原生复选框处理鼠标、键盘和自动化切换，外观由主题模板提供。
/// </summary>
public partial class ToggleSwitch : UserControl
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(ToggleSwitch),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOnChanged));

    public static readonly RoutedEvent ChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(Changed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ToggleSwitch));

    public ToggleSwitch()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler Changed
    {
        add => AddHandler(ChangedEvent, value);
        remove => RemoveHandler(ChangedEvent, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    private static void OnIsOnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (ToggleSwitch)sender;
        control.RaiseEvent(new RoutedEventArgs(ChangedEvent, control));
    }
}
