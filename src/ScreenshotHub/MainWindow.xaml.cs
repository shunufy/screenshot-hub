using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using Microsoft.Win32;
using ScreenshotHub.Core;
using ScreenshotHub.Services;
using ScreenshotHub.ViewModels;

namespace ScreenshotHub;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(IReadOnlyList<string>? restrictedScanRoots = null, string? settingsPath = null)
    {
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(AppText.LanguageCode);
        DateFromPicker.Language = Language;
        DateToPicker.Language = Language;
        _viewModel = new MainWindowViewModel(restrictedScanRoots, settingsPath);
        DataContext = _viewModel;
        _viewModel.VisibleItems.CollectionChanged += VisibleItems_CollectionChanged;
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    internal MainWindowViewModel ViewModel => _viewModel;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _viewModel.VisibleItems.CollectionChanged -= VisibleItems_CollectionChanged;
        _viewModel.Dispose();
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditFolders)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = AppText.SelectScreenshotFolder,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.AddCustomRootAsync(dialog.FolderName);
        }
    }

    private void Thumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Focus();
        }

        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            OpenItem(item);
            e.Handled = true;
        }
    }

    private void Thumbnail_KeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Space) &&
            sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            OpenItem(item);
            e.Handled = true;
        }
    }

    private void VisibleItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            GalleryScroller.ScrollToTop();
        }
    }

    private void OpenViewerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            OpenItem(item);
        }
    }

    private void OpenDefaultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            OpenItemInDefaultApp(item);
        }
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            e.Handled = true;
            await _viewModel.ToggleFavoriteAsync(item.FilePath);
        }
    }

    private async void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            await _viewModel.ToggleFavoriteAsync(item.FilePath);
        }
    }

    private async void EditTagsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            await EditTagsAsync(item);
        }
    }

    private void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            try
            {
                if (!ShellService.ShowInExplorer(item.FilePath))
                {
                    ShowMissingFileMessage();
                }
            }
            catch (Exception exception)
            {
                ShowActionError(exception);
            }
        }
    }

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenshotItemViewModel item })
        {
            try
            {
                ShellService.CopyPath(item.FilePath);
            }
            catch (Exception exception)
            {
                ShowActionError(exception);
            }
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderViewModel { Path: { Length: > 0 } path } })
        {
            return;
        }

        try
        {
            if (!ShellService.OpenFolder(path))
            {
                ShowMissingFolderMessage();
            }
        }
        catch (Exception exception)
        {
            ShowActionError(exception);
        }
    }

    private void OpenItem(ScreenshotItemViewModel item)
    {
        var records = _viewModel.GetViewerRecords();
        if (!records.Any(record => string.Equals(
                record.FilePath,
                item.FilePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            records = [item.Record];
        }

        var viewer = new ImageViewerWindow(_viewModel, records, item.FilePath)
        {
            Owner = this
        };
        viewer.Show();
    }

    private void OpenItemInDefaultApp(ScreenshotItemViewModel item)
    {
        try
        {
            if (!ShellService.OpenFile(item.FilePath))
            {
                ShowMissingFileMessage();
            }
        }
        catch (Exception exception)
        {
            ShowActionError(exception);
        }
    }

    private async Task EditTagsAsync(ScreenshotItemViewModel item)
    {
        var editor = new TagEditorWindow(_viewModel.GetUserDataSnapshot(item.FilePath).Tags)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            await _viewModel.UpdateTagsAsync(item.FilePath, editor.Tags);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && _viewModel.ScanCommand.CanExecute(null))
        {
            _viewModel.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.CancelScanCommand.CanExecute(null))
        {
            _viewModel.CancelScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.CancelAnalysisCommand.CanExecute(null))
        {
            _viewModel.CancelAnalysisCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ShowMissingFileMessage()
        => MessageBox.Show(
            this,
            AppText.MissingImageMessage,
            AppText.WindowTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void ShowMissingFolderMessage()
        => MessageBox.Show(
            this,
            AppText.MissingFolderMessage,
            AppText.WindowTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void ShowActionError(Exception exception)
        => MessageBox.Show(
            this,
            exception.Message,
            AppText.ActionFailedTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows versions simply keep the system title-bar theme.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
