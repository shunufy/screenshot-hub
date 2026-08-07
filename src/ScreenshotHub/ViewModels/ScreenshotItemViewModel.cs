using System.IO;
using System.Windows.Media;
using ScreenshotHub.Core;
using ScreenshotHub.Infrastructure;
using ScreenshotHub.Services;

namespace ScreenshotHub.ViewModels;

internal sealed class ScreenshotItemViewModel : ObservableObject
{
    private ImageSource? _thumbnail;
    private bool _isThumbnailLoading = true;
    private bool _isThumbnailUnavailable;

    public ScreenshotItemViewModel(ScreenshotRecord record)
    {
        Record = record;
        FileName = System.IO.Path.GetFileName(record.FilePath);
        FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(record.FilePath);
        FolderName = System.IO.Path.GetFileName(record.LibraryFolder.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        TimeText = record.LastWriteTimeUtc.ToLocalTime().ToString("yyyy/M/d  HH:mm");
        SizeText = FormatFileSize(record.FileSize);
    }

    public ScreenshotRecord Record { get; }
    public string FilePath => Record.FilePath;
    public string FileName { get; }
    public string FileNameWithoutExtension { get; }
    public string FolderName { get; }
    public string GameName => Record.GameName;
    public string TimeText { get; }
    public string SizeText { get; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set => SetProperty(ref _isThumbnailLoading, value);
    }

    public bool IsThumbnailUnavailable
    {
        get => _isThumbnailUnavailable;
        private set => SetProperty(ref _isThumbnailUnavailable, value);
    }

    public async Task LoadThumbnailAsync(ThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        try
        {
            var source = await thumbnailService.GetAsync(
                Record.FilePath,
                Record.LastWriteTimeUtc,
                Record.FileSize,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Thumbnail = source;
            IsThumbnailUnavailable = source is null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            IsThumbnailUnavailable = true;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsThumbnailLoading = false;
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.0} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d):0.0} MB";
        }

        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:0} KB";
        }

        return $"{bytes:N0} B";
    }
}
