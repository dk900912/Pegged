using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Pegged.Views;

/// <summary>
/// A small switch. Click anywhere on it to flip. The track color and the
/// knob position are animated with one short easing storyboard.
/// </summary>
public partial class ToggleSwitch : UserControl
{
    private bool _isOn;

    public ToggleSwitch()
    {
        InitializeComponent();
    }

    public event EventHandler? Changed;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            Update();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => IsOn = !IsOn;

    private void Update()
    {
        var accent = TryFindResource("Brush.Accent") as Brush
                     ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
        var muted = TryFindResource("Brush.SurfaceAlt") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFC));

        AnimateFill(Track, _isOn ? accent : muted);
        AnimateMargin(_isOn ? 23 : 3);
    }

    private void AnimateFill(Shape shape, Brush target)
    {
        var anim = new ColorAnimation
        {
            To = ((SolidColorBrush)target).Color,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        shape.Fill.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void AnimateMargin(double to)
    {
        var anim = new ThicknessAnimation
        {
            To = new Thickness(to, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Knob.BeginAnimation(MarginProperty, anim);
    }
}
