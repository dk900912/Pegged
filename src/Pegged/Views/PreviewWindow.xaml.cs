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
            // Load the pixels now and let go of the file. A BitmapImage built
            // from a Uri keeps the file open until it is collected, and while
            // it is open the screenshot cannot be copied (the clipboard gets
            // only the file drop) or moved to the Recycle Bin - taking the
            // card down failed with "some operations were aborted" and the
            // card simply stayed on the line.
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            BigImage.Source = image;
        }
        catch
        {
            // For videos and exotic formats, the image stays empty and the
            // "Open in default app" button is the only path forward.
        }

        PreviewKeyDown += OnPreviewKeyDown;

        // Shown from the line, which never activates: without this the window
        // appears but the keyboard still belongs to the app in front of it,
        // so Esc does nothing until the window is clicked.
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => WindowFocus.Take(this)));
    }

    protected override void OnClosed(EventArgs e)
    {
        // Hand the foreground back to whatever had it before the preview, or
        // the system is left pointing at a window that no longer exists.
        WindowFocus.Release();
        base.OnClosed(e);
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
