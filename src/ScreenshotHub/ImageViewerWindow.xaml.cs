using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenshotHub.Core;
using ScreenshotHub.Services;
using ScreenshotHub.ViewModels;

namespace ScreenshotHub;

public partial class ImageViewerWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyList<ScreenshotRecord> _records;
    private int _index;
    private double _zoom = 1;
    private bool _fitMode = true;
    private bool _isPanning;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    internal ImageViewerWindow(
        MainWindowViewModel viewModel,
        IReadOnlyList<ScreenshotRecord> records,
        string initialPath)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _records = records.Count > 0 ? records : throw new ArgumentException("The viewer requires an image.", nameof(records));
        _index = Math.Max(0, records
            .Select((record, index) => (record, index))
            .FirstOrDefault(pair => string.Equals(
                pair.record.FilePath,
                initialPath,
                StringComparison.OrdinalIgnoreCase)).index);
        Loaded += (_, _) => LoadCurrentImage();
        SizeChanged += (_, _) =>
        {
            if (_fitMode)
            {
                FitImage();
            }
        };
    }

    private ScreenshotRecord Current => _records[_index];

    private void LoadCurrentImage()
    {
        ViewerImage.Source = null;
        UnavailableText.Visibility = Visibility.Collapsed;
        try
        {
            using var stream = new FileStream(
                Current.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                128 * 1024,
                FileOptions.SequentialScan);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault() ?? throw new FileFormatException();
            frame.Freeze();
            ViewerImage.Source = frame;
            ViewerImage.Width = frame.PixelWidth;
            ViewerImage.Height = frame.PixelHeight;
            _fitMode = true;
            Dispatcher.BeginInvoke(FitImage, DispatcherPriority.Loaded);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            UnavailableText.Visibility = Visibility.Visible;
        }

        RefreshDetails();
    }

    private void RefreshDetails()
    {
        FileNameText.Text = Path.GetFileName(Current.FilePath);
        FileNameText.ToolTip = Current.FilePath;
        PathText.Text = Current.FilePath;
        PathText.ToolTip = Current.FilePath;
        PositionText.Text = $"{_index + 1:N0} / {_records.Count:N0}";
        PreviousButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index + 1 < _records.Count;

        var userData = _viewModel.GetUserDataSnapshot(Current.FilePath);
        FavoriteButton.Content = userData.IsFavorite ? "★" : "☆";
        FavoriteButton.ToolTip = userData.IsFavorite ? AppText.RemoveFavorite : AppText.AddFavorite;
        TagsText.Text = userData.Tags.Count > 0
            ? $"{AppText.Tags}: {string.Join(" · ", userData.Tags)}"
            : $"{AppText.Tags}: —";
        var duplicate = _viewModel.GetDuplicateMembership(Current.FilePath);
        DuplicateText.Text = duplicate.IsExactDuplicate
            ? AppText.ExactCopies(duplicate.ExactGroupCount)
            : duplicate.IsSimilar
                ? AppText.SimilarCopies(duplicate.SimilarGroupCount)
                : string.Empty;
        Title = $"{Path.GetFileName(Current.FilePath)} — {AppText.ViewerTitle}";
    }

    private void Navigate(int offset)
    {
        var next = Math.Clamp(_index + offset, 0, _records.Count - 1);
        if (next == _index)
        {
            return;
        }

        _index = next;
        LoadCurrentImage();
    }

    private void SetZoom(double zoom, bool fitMode = false)
    {
        _zoom = Math.Clamp(zoom, 0.03, 8);
        _fitMode = fitMode;
        ViewerScale.ScaleX = _zoom;
        ViewerScale.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void FitImage()
    {
        if (ViewerImage.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return;
        }

        var width = ViewerScrollViewer.ViewportWidth > 0
            ? ViewerScrollViewer.ViewportWidth - 18
            : ViewerScrollViewer.ActualWidth - 18;
        var height = ViewerScrollViewer.ViewportHeight > 0
            ? ViewerScrollViewer.ViewportHeight - 18
            : ViewerScrollViewer.ActualHeight - 18;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        SetZoom(Math.Min(width / bitmap.PixelWidth, height / bitmap.PixelHeight), fitMode: true);
        ViewerScrollViewer.ScrollToHome();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.2);
    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.2);
    private void FitButton_Click(object sender, RoutedEventArgs e) => FitImage();
    private void ActualSizeButton_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleFavoriteAsync(Current.FilePath);
        RefreshDetails();
    }

    private async void EditTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TagEditorWindow(_viewModel.GetUserDataSnapshot(Current.FilePath).Tags)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            await _viewModel.UpdateTagsAsync(Current.FilePath, editor.Tags);
            RefreshDetails();
        }
    }

    private void ShowInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ShellService.ShowInExplorer(Current.FilePath))
            {
                ShowMissingImage();
            }
        }
        catch (Exception exception)
        {
            ShowActionError(exception);
        }
    }

    private void OpenDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ShellService.OpenFile(Current.FilePath))
            {
                ShowMissingImage();
            }
        }
        catch (Exception exception)
        {
            ShowActionError(exception);
        }
    }

    private void ViewerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        SetZoom(e.Delta > 0 ? _zoom * 1.12 : _zoom / 1.12);
        e.Handled = true;
    }

    private void ViewerScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _panStart = e.GetPosition(ViewerScrollViewer);
        _panHorizontalOffset = ViewerScrollViewer.HorizontalOffset;
        _panVerticalOffset = ViewerScrollViewer.VerticalOffset;
        ViewerScrollViewer.CaptureMouse();
        ViewerScrollViewer.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void ViewerScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(ViewerScrollViewer);
        ViewerScrollViewer.ScrollToHorizontalOffset(_panHorizontalOffset - (point.X - _panStart.X));
        ViewerScrollViewer.ScrollToVerticalOffset(_panVerticalOffset - (point.Y - _panStart.Y));
    }

    private void ViewerScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => StopPanning();
    private void ViewerScrollViewer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPanning();
        }
    }

    private void StopPanning()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ViewerScrollViewer.ReleaseMouseCapture();
        ViewerScrollViewer.Cursor = Cursors.Arrow;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                Navigate(-1);
                e.Handled = true;
                break;
            case Key.Right:
                Navigate(1);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                SetZoom(_zoom * 1.2);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                SetZoom(_zoom / 1.2);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                FitImage();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                SetZoom(1);
                e.Handled = true;
                break;
        }
    }

    private void ShowMissingImage()
        => MessageBox.Show(this, AppText.MissingImageMessage, AppText.WindowTitle,
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowActionError(Exception exception)
        => MessageBox.Show(this, exception.Message, AppText.ActionFailedTitle,
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
