using System.Windows;
using System.Windows.Automation.Peers;
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

    /// <summary>
    /// This wrapper is a decoration around a real CheckBox, and the CheckBox
    /// carries the same automation id on purpose (that is what the settings
    /// window names). Two elements with one id is not a usable switch: UI
    /// Automation reports the wrapper as a second "ClipboardSwitch" and any
    /// FindFirst on the id is a coin toss. No peer here means the wrapper does
    /// not exist as far as assistive tech is concerned and the CheckBox inside
    /// is the switch - which it is.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => null!;
}
