using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenshotHub.Core;
using ScreenshotHub.Infrastructure;
using ScreenshotHub.Services;

namespace ScreenshotHub.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 240;

    private readonly SettingsStore _settingsStore;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<string> _sessionRoots;
    private readonly bool _restrictToSessionRoots;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly RelayCommand _cancelScanCommand;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _removeRootCommand;
    private readonly List<ScreenshotRecord> _records = new();
    private readonly List<ScreenshotRecord> _filteredRecords = new();

    private HubSettings _settings = new();
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private FolderViewModel? _selectedFolder;
    private string _searchText = string.Empty;
    private string _statusText = AppText.Preparing;
    private string _statusDetail = AppText.PreparingDetail;
    private string _lastUpdatedText = AppText.NotScanned;
    private bool _isScanning;
    private bool _isInitialized;
    private bool _isApplyingSettings;
    private bool _autoRefresh;
    private int _autoRefreshMinutes = 5;
    private int _pageIndex;

    public MainWindowViewModel(
        IReadOnlyList<string>? restrictedScanRoots = null,
        string? settingsPath = null)
    {
        _settingsStore = new SettingsStore(settingsPath);
        _restrictToSessionRoots = restrictedScanRoots is { Count: > 0 };
        _sessionRoots = (restrictedScanRoots ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _scanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsScanning);
        _cancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        _previousPageCommand = new RelayCommand(_ => SetPage(PageIndex - 1), _ => PageIndex > 0);
        _nextPageCommand = new RelayCommand(_ => SetPage(PageIndex + 1), _ => PageIndex + 1 < PageCount);
        _removeRootCommand = new AsyncRelayCommand(
            parameter => parameter is FolderViewModel folder
                ? RemoveCustomRootAsync(folder)
                : Task.CompletedTask,
            parameter => parameter is FolderViewModel { CanRemove: true } && !IsScanning);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
        _refreshTimer.Tick += RefreshTimerOnTick;
    }

    public ObservableCollection<FolderViewModel> Folders { get; } = new();
    public ObservableCollection<ScreenshotItemViewModel> VisibleItems { get; } = new();
    public IReadOnlyList<int> RefreshIntervals { get; } = [1, 5, 10, 30, 60];

    public ICommand ScanCommand => _scanCommand;
    public ICommand CancelScanCommand => _cancelScanCommand;
    public ICommand PreviousPageCommand => _previousPageCommand;
    public ICommand NextPageCommand => _nextPageCommand;
    public ICommand RemoveRootCommand => _removeRootCommand;

    public FolderViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                OnPropertyChanged(nameof(CollectionTitle));
                OnPropertyChanged(nameof(CollectionSubtitle));
                ApplyFilter(resetPage: true);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter(resetPage: true);
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanEditFolders));
            _scanCommand.RaiseCanExecuteChanged();
            _cancelScanCommand.RaiseCanExecuteChanged();
            _removeRootCommand.RaiseCanExecuteChanged();
        }
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (!SetProperty(ref _autoRefresh, value))
            {
                return;
            }

            if (!_isApplyingSettings)
            {
                _settings.AutoRefresh = value;
                ConfigureRefreshTimer();
                _ = SaveSettingsQuietlyAsync();
            }
        }
    }

    public int AutoRefreshMinutes
    {
        get => _autoRefreshMinutes;
        set
        {
            var safeValue = Math.Clamp(value, 1, 120);
            if (!SetProperty(ref _autoRefreshMinutes, safeValue))
            {
                return;
            }

            if (!_isApplyingSettings)
            {
                _settings.AutoRefreshMinutes = safeValue;
                ConfigureRefreshTimer();
                _ = SaveSettingsQuietlyAsync();
            }
        }
    }

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(ResultRangeText));
                _previousPageCommand.RaiseCanExecuteChanged();
                _nextPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int PageCount => Math.Max(1, (int)Math.Ceiling(_filteredRecords.Count / (double)PageSize));
    public bool IsLoading => IsScanning && _records.Count == 0;
    public bool CanEditFolders => !IsScanning;
    public bool HasVisibleItems => VisibleItems.Count > 0;
    public bool IsEmpty => !IsScanning && VisibleItems.Count == 0;
    public bool HasMultiplePages => PageCount > 1;

    public string CollectionTitle => SelectedFolder?.DisplayName ?? AppText.AllScreenshots;

    public string CollectionSubtitle => _filteredRecords.Count == 0
        ? AppText.NoScreenshotsYet
        : AppText.CollectionSummary(_filteredRecords.Count);

    public string PageText => $"{PageIndex + 1} / {PageCount}";

    public string ResultRangeText
    {
        get
        {
            if (_filteredRecords.Count == 0)
            {
                return AppText.ImageCount(0);
            }

            var start = PageIndex * PageSize + 1;
            var end = Math.Min(start + PageSize - 1, _filteredRecords.Count);
            return AppText.PageRange(start, end, _filteredRecords.Count);
        }
    }

    public string EmptyTitle => _records.Count switch
    {
        0 => AppText.EmptyNotFound,
        _ when !string.IsNullOrWhiteSpace(SearchText) => AppText.EmptySearch,
        _ => AppText.EmptyCollection
    };

    public string EmptyDetail => _records.Count == 0
        ? AppText.EmptyInitialHint
        : AppText.EmptyFilteredHint;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            _settings = await _settingsStore.LoadAsync();
        }
        catch (Exception exception)
        {
            _settings = new HubSettings();
            StatusText = AppText.SettingsLoadFailed;
            StatusDetail = exception.Message;
        }

        _isApplyingSettings = true;
        AutoRefresh = _settings.AutoRefresh;
        AutoRefreshMinutes = _settings.AutoRefreshMinutes;
        _isApplyingSettings = false;
        ConfigureRefreshTimer();

        if (_settings.ScanOnStartup || _restrictToSessionRoots)
        {
            await ScanAsync();
        }
        else
        {
            RebuildFolders([], BuildRoots());
            StatusText = AppText.WaitingToScan;
            StatusDetail = AppText.WaitingToScanDetail;
        }
    }

    public async Task AddCustomRootAsync(string folderPath)
    {
        if (IsScanning)
        {
            StatusText = AppText.CannotAddWhileScanning;
            StatusDetail = AppText.CannotAddWhileScanningDetail;
            return;
        }

        var normalized = NormalizePath(folderPath);
        if (normalized is null || !Directory.Exists(normalized))
        {
            StatusText = AppText.AddFolderFailed;
            StatusDetail = AppText.FolderAccessFailed;
            return;
        }

        if (IsDriveRoot(normalized))
        {
            StatusText = AppText.CannotAddDriveRoot;
            StatusDetail = AppText.CannotAddDriveRootDetail;
            return;
        }

        if (!_settings.CustomRoots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _settings.CustomRoots.Add(normalized);
        }

        _settings.IgnoredRoots.RemoveAll(path =>
            string.Equals(NormalizePath(path), normalized, StringComparison.OrdinalIgnoreCase));

        if (_restrictToSessionRoots && !_sessionRoots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _sessionRoots.Add(normalized);
        }

        await SaveSettingsQuietlyAsync();
        await ScanAsync();
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        _refreshTimer.Stop();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;
        IsScanning = true;
        StatusText = AppText.SearchingScreenshotsProgress;
        StatusDetail = AppText.CheckingLocations;

        var progress = new Progress<ScanProgress>(value =>
        {
            StatusText = string.IsNullOrWhiteSpace(value.CurrentPath)
                ? AppText.SearchingScreenshotsProgress
                : AppText.CheckingPath(ShortenPath(value.CurrentPath));
            StatusDetail = AppText.ScanProgress(value.DirectoriesVisited, value.ScreenshotCount);
        });

        try
        {
            var roots = BuildRoots();
            var result = await ScreenshotScanner.ScanAsync(
                roots,
                Math.Clamp(_settings.MaxDepth, 1, 64),
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _records.Clear();
            var ignoredRoots = _settings.IgnoredRoots
                .Select(NormalizePath)
                .Where(path => path is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _records.AddRange(result.Screenshots
                .Where(record => !ignoredRoots.Any(root => IsSameOrDescendant(record.FilePath, root)))
                .OrderByDescending(record => record.LastWriteTimeUtc)
                .ThenBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase));

            RebuildFolders(_records, result.ScannedRoots);
            ApplyFilter(resetPage: true);

            LastUpdatedText = AppText.Updated(DateTime.Now);
            StatusText = AppText.ScreenshotsFound(_records.Count);
            StatusDetail = result.Warnings.Count == 0
                ? AppText.FoldersChecked(result.DirectoriesVisited, FormatDuration(result.Duration))
                : AppText.CompletedWithWarnings(result.Warnings.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = AppText.ScanCancelled;
            StatusDetail = _records.Count == 0
                ? AppText.ListNotUpdated
                : AppText.PreviousImages(_records.Count);
        }
        catch (Exception exception)
        {
            StatusText = AppText.ScanFailed;
            StatusDetail = exception.Message;
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsEmpty));
            ConfigureRefreshTimer();
        }
    }

    private IReadOnlyList<ScanRoot> BuildRoots()
    {
        if (_restrictToSessionRoots)
        {
            return _sessionRoots
                .Select(path => new ScanRoot(path, GetFolderName(path), true))
                .ToArray();
        }

        return KnownScanRoots.Discover(_settings);
    }

    private void RebuildFolders(
        IReadOnlyCollection<ScreenshotRecord> records,
        IReadOnlyCollection<ScanRoot> scannedRoots)
    {
        var selectedPath = SelectedFolder?.Path;
        var customRoots = scannedRoots
            .Where(root => root.IsCustom)
            .Select(root => root.Path)
            .Concat(_settings.CustomRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var folders = records
            .GroupBy(record => record.LibraryFolder, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var customRoot = customRoots
                    .OrderByDescending(path => path.Length)
                    .FirstOrDefault(path => IsSameOrDescendant(group.Key, path));
                var displayName = group
                    .Select(record => record.GameName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? GetFolderName(group.Key);

                return new FolderViewModel(displayName, group.Key, customRootPath: customRoot)
                {
                    Count = group.Count(),
                    LatestUtc = group.Max(record => record.LastWriteTimeUtc)
                };
            })
            .ToList();

        foreach (var root in scannedRoots)
        {
            if (folders.Any(folder =>
                    folder.Path is not null && IsSameOrDescendant(folder.Path, root.Path)))
            {
                continue;
            }

            folders.Add(new FolderViewModel(
                root.DisplayName,
                root.Path,
                customRootPath: root.IsCustom ? root.Path : null));
        }

        var allFolder = new FolderViewModel(AppText.AllScreenshots, null, isAll: true)
        {
            Count = records.Count,
            LatestUtc = records.Count > 0 ? records.Max(record => record.LastWriteTimeUtc) : null
        };

        Folders.Clear();
        Folders.Add(allFolder);
        foreach (var folder in folders
                     .OrderByDescending(folder => folder.LatestUtc ?? DateTime.MinValue)
                     .ThenBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Folders.Add(folder);
        }

        SelectedFolder = selectedPath is null
            ? allFolder
            : Folders.FirstOrDefault(folder =>
                string.Equals(folder.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? allFolder;
    }

    private void ApplyFilter(bool resetPage)
    {
        if (resetPage)
        {
            PageIndex = 0;
        }

        var folderPath = SelectedFolder?.Path;
        var query = SearchText.Trim();

        _filteredRecords.Clear();
        _filteredRecords.AddRange(_records.Where(record =>
        {
            if (!string.IsNullOrWhiteSpace(folderPath) &&
                !string.Equals(record.LibraryFolder, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return query.Length == 0 ||
                   record.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   record.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }));

        if (PageIndex >= PageCount)
        {
            PageIndex = Math.Max(0, PageCount - 1);
        }

        ShowCurrentPage();
        NotifyGalleryStateChanged();
    }

    private void SetPage(int pageIndex)
    {
        var safeIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        if (safeIndex == PageIndex)
        {
            return;
        }

        PageIndex = safeIndex;
        ShowCurrentPage();
        NotifyGalleryStateChanged();
    }

    private void ShowCurrentPage()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        var cancellationToken = _thumbnailCancellation.Token;

        VisibleItems.Clear();
        foreach (var record in _filteredRecords
                     .Skip(PageIndex * PageSize)
                     .Take(PageSize))
        {
            VisibleItems.Add(new ScreenshotItemViewModel(record));
        }

        _ = LoadVisibleThumbnailsAsync(VisibleItems.ToArray(), cancellationToken);
    }

    private async Task LoadVisibleThumbnailsAsync(
        IReadOnlyList<ScreenshotItemViewModel> items,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(items.Select(item =>
                item.LoadThumbnailAsync(_thumbnailService, cancellationToken)));
        }
        catch (OperationCanceledException)
        {
            // A collection/search/page change superseded this thumbnail batch.
        }
    }

    private async Task RemoveCustomRootAsync(FolderViewModel folder)
    {
        if (folder.CustomRootPath is not { Length: > 0 } rootPath)
        {
            return;
        }

        _settings.CustomRoots.RemoveAll(path =>
            string.Equals(NormalizePath(path), NormalizePath(rootPath), StringComparison.OrdinalIgnoreCase));
        var normalizedRoot = NormalizePath(rootPath);
        if (normalizedRoot is not null &&
            !_settings.IgnoredRoots.Contains(normalizedRoot, StringComparer.OrdinalIgnoreCase))
        {
            _settings.IgnoredRoots.Add(normalizedRoot);
        }
        _sessionRoots.RemoveAll(path =>
            string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase));
        await SaveSettingsQuietlyAsync();
        await ScanAsync();
    }

    private async Task SaveSettingsQuietlyAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            StatusText = AppText.SettingsSaveFailed;
            StatusDetail = exception.Message;
        }
    }

    private void ConfigureRefreshTimer()
    {
        _refreshTimer.Stop();
        if (!AutoRefresh || IsScanning || !_isInitialized)
        {
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromMinutes(AutoRefreshMinutes);
        _refreshTimer.Start();
    }

    private async void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        await ScanAsync();
    }

    private void NotifyGalleryStateChanged()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(ResultRangeText));
        OnPropertyChanged(nameof(HasVisibleItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(CollectionSubtitle));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        _previousPageCommand.RaiseCanExecuteChanged();
        _nextPageCommand.RaiseCanExecuteChanged();
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedCandidate = NormalizePath(candidate);
        var normalizedRoot = NormalizePath(root);
        if (normalizedCandidate is null || normalizedRoot is null)
        {
            return false;
        }

        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        return string.Equals(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFolderName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
    }

    private static string ShortenPath(string path)
        => path.Length <= 74 ? path : $"…{path[^71..]}";

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:0} ms"
            : AppText.Seconds(duration.TotalSeconds);

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimerOnTick;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();

    }
}
