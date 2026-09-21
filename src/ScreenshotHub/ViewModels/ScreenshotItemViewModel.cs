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
    private bool _isFavorite;
    private IReadOnlyList<string> _tags = [];
    private DuplicateMembership _duplicateMembership = new(null, 0, null, 0);

    public ScreenshotItemViewModel(
        ScreenshotRecord record,
        ScreenshotUserData? userData = null,
        DuplicateMembership? duplicateMembership = null)
    {
        Record = record;
        FileName = System.IO.Path.GetFileName(record.FilePath);
        FileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(record.FilePath);
        FolderName = System.IO.Path.GetFileName(record.LibraryFolder.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        TimeText = record.LastWriteTimeUtc.ToLocalTime().ToString("yyyy/M/d  HH:mm");
        SizeText = FormatFileSize(record.FileSize);
        ApplyUserData(userData);
        ApplyDuplicateMembership(duplicateMembership);
    }

    public ScreenshotRecord Record { get; }
    public string FilePath => Record.FilePath;
    public string FileName { get; }
    public string FileNameWithoutExtension { get; }
    public string FolderName { get; }
    public string GameName => Record.GameName;
    public string TimeText { get; }
    public string SizeText { get; }
    public bool IsFavorite
    {
        get => _isFavorite;
        private set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteAutomationName));
            }
        }
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        private set
        {
            _tags = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(HasTags));
        }
    }

    public string TagsText => string.Join(" · ", Tags);
    public bool HasTags => Tags.Count > 0;
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteAutomationName => IsFavorite ? AppText.RemoveFavorite : AppText.AddFavorite;
    public DuplicateMembership DuplicateMembership
    {
        get => _duplicateMembership;
        private set
        {
            _duplicateMembership = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExactDuplicate));
            OnPropertyChanged(nameof(IsSimilarImage));
            OnPropertyChanged(nameof(DuplicateBadgeText));
            OnPropertyChanged(nameof(HasDuplicateBadge));
        }
    }

    public bool IsExactDuplicate => DuplicateMembership.IsExactDuplicate;
    public bool IsSimilarImage => DuplicateMembership.IsSimilar;
    public bool HasDuplicateBadge => IsExactDuplicate || IsSimilarImage;
    public string DuplicateBadgeText => IsExactDuplicate
        ? AppText.ExactCopies(DuplicateMembership.ExactGroupCount)
        : IsSimilarImage
            ? AppText.SimilarCopies(DuplicateMembership.SimilarGroupCount)
            : string.Empty;

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

    public void ApplyUserData(ScreenshotUserData? userData)
    {
        IsFavorite = userData?.IsFavorite == true;
        Tags = userData?.Tags?.ToArray() ?? [];
    }

    public void ApplyDuplicateMembership(DuplicateMembership? membership)
        => DuplicateMembership = membership ?? new DuplicateMembership(null, 0, null, 0);

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
