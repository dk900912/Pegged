using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Pegged.Core;
using Pegged.Localization;

namespace Pegged.Views;

/// <summary>
/// A borderless preview: shows the picture at full size, with "Open in app"
/// and "Crop" actions, Esc to close.
/// </summary>
public partial class PreviewWindow : Window
{
    public string FilePath { get; }

    public event EventHandler<string>? CropRequested;

    public PreviewWindow(string path)
    {
        FilePath = path;
        InitializeComponent();

        TitleText.Text = Path.GetFileName(path);

        try
        {
            BigImage.Source = new BitmapImage(new Uri(path));
        }
        catch
        {
            // For videos and exotic formats, the image stays empty and the
            // "Open in default app" button is the only path forward.
        }

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.Get("Dialog.LoginFailed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCrop(object sender, RoutedEventArgs e)
    {
        CropRequested?.Invoke(this, FilePath);
        Close();
    }
}
