using ScreenshotHub.Infrastructure;
using ScreenshotHub.Core;

namespace ScreenshotHub.ViewModels;

internal sealed class FolderViewModel : ObservableObject
{
    private int _count;
    private DateTime? _latestUtc;

    public FolderViewModel(
        string displayName,
        string? path,
        bool isAll = false,
        string? customRootPath = null)
    {
        DisplayName = displayName;
        Path = path;
        IsAll = isAll;
        CustomRootPath = customRootPath;
    }

    public string DisplayName { get; }
    public string? Path { get; }
    public bool IsAll { get; }
    public string? CustomRootPath { get; }
    public bool CanRemove => !IsAll && !string.IsNullOrWhiteSpace(CustomRootPath);

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public DateTime? LatestUtc
    {
        get => _latestUtc;
        set
        {
            if (SetProperty(ref _latestUtc, value))
            {
                OnPropertyChanged(nameof(LatestText));
            }
        }
    }

    public string CountText => Count.ToString("N0");

    public string PathText => IsAll ? AppText.AllDetectedFolders : Path ?? "";

    public string LatestText => LatestUtc is { } utc
        ? AppText.Latest(utc.ToLocalTime())
        : AppText.NoImages;
}
