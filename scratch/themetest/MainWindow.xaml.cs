using System;
using System.Windows;
using System.Windows.Media;

namespace T;

public partial class MainWindow : Window
{
    private ResourceDictionary? _dark;

    public MainWindow() => InitializeComponent();

    private void OnDark(object sender, RoutedEventArgs e)
    {
        var app = Application.Current;
        if (app is null) return;
        _dark ??= new ResourceDictionary { Source = new Uri("Dark.xaml", UriKind.Relative) };
        if (!app.Resources.MergedDictionaries.Contains(_dark))
            app.Resources.MergedDictionaries.Add(_dark);

        var card = (SolidColorBrush)Card.Background;
        var text = (SolidColorBrush)Words.Foreground;
        var byKey = (SolidColorBrush)app.TryFindResource("Brush.Card")!;
        System.IO.File.AppendAllText(@"C:\Users\dk900\WorkBuddy AI\win11-tendedero\scratch\themetest\result.txt",
            $"card={card.Color} text={text.Color} bykey={byKey.Color}\r\n");
    }
}
