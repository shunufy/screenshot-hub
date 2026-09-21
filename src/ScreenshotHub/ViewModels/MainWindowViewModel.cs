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
    public const int PageSize = 80;

    private readonly SettingsStore _settingsStore;
    private readonly ScreenshotCatalogStore _catalogStore;
    private readonly ScreenshotFolderCatalogStore _folderCatalogStore;
    private readonly ScreenshotUserDataStore _userDataStore;
    private readonly ScreenshotAnalysisStore _analysisStore;
    private readonly ThumbnailService _thumbnailService = new();
    private readonly ImageAnalysisService _imageAnalysisService = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly List<string> _sessionRoots;
    private readonly bool _restrictToSessionRoots;
    private readonly AsyncRelayCommand _scanCommand;
    private readonly RelayCommand _cancelScanCommand;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;
    private readonly AsyncRelayCommand _removeRootCommand;
    private readonly AsyncRelayCommand _analyzeCommand;
    private readonly RelayCommand _cancelAnalysisCommand;
    private readonly List<ScreenshotRecord> _records = new();
    private readonly List<ScreenshotRecord> _filteredRecords = new();
    private readonly Dictionary<string, ScreenshotUserData> _userData =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenshotAnalysisRecord> _analyses =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, DuplicateMembership> _duplicateMemberships =
        new Dictionary<string, DuplicateMembership>(StringComparer.OrdinalIgnoreCase);

    private HubSettings _settings = HubSettings.CreateDefault();
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private CancellationTokenSource? _analysisCancellation;
    private FolderViewModel? _selectedFolder;
    private string _searchText = string.Empty;
    private string _statusText = AppText.Preparing;
    private string _statusDetail = AppText.PreparingDetail;
    private string _lastUpdatedText = AppText.NotScanned;
    private bool _isScanning;
    private bool _isInitialized;
    private bool _isApplyingSettings;
    private bool _isRebuildingFolders;
    private bool _isRestoringCatalog;
    private bool _imageCatalogLoaded;
    private bool _hasPersistedCatalog;
    private bool _autoRefresh;
    private int _autoRefreshMinutes = 30;
    private bool _folderOnlyMode;
    private IReadOnlyList<FolderViewModel> _visibleFolders = [];
    private int _knownScreenshotCount;
    private DateTime _catalogScanUtc;
    private DateTime? _folderCatalogScanUtc;
    private int _pageIndex;
    private bool _isAnalyzing;
    private GalleryFilterOption _selectedGalleryFilter;
    private TagFilterOption _selectedTagFilter;
    private BrowseOption _selectedDatePeriod;
    private BrowseOption _selectedSortOrder;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;

    public MainWindowViewModel(
        IReadOnlyList<string>? restrictedScanRoots = null,
        string? settingsPath = null)
    {
        _settingsStore = new SettingsStore(settingsPath);
        _catalogStore = new ScreenshotCatalogStore(
            ScreenshotCatalogStore.ResolvePathForSettings(settingsPath));
        _folderCatalogStore = new ScreenshotFolderCatalogStore(
            ScreenshotFolderCatalogStore.ResolvePathForSettings(settingsPath));
        _userDataStore = new ScreenshotUserDataStore(
            ScreenshotUserDataStore.ResolvePathForSettings(settingsPath));
        _analysisStore = new ScreenshotAnalysisStore(
            ScreenshotAnalysisStore.ResolvePathForSettings(settingsPath));
        _restrictToSessionRoots = restrictedScanRoots is { Count: > 0 };
        _sessionRoots = (restrictedScanRoots ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _scanCommand = new AsyncRelayCommand(
            _ => ScanAsync(),
            _ => !IsScanning && !IsAnalyzing && !_isRestoringCatalog);
        _cancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        _previousPageCommand = new RelayCommand(_ => SetPage(PageIndex - 1), _ => PageIndex > 0);
        _nextPageCommand = new RelayCommand(_ => SetPage(PageIndex + 1), _ => PageIndex + 1 < PageCount);
        _removeRootCommand = new AsyncRelayCommand(
            parameter => parameter is FolderViewModel folder
                ? RemoveCustomRootAsync(folder)
                : Task.CompletedTask,
            parameter => parameter is FolderViewModel { CanRemove: true } &&
                         !IsScanning &&
                         !IsAnalyzing &&
                         !_isRestoringCatalog);
        GalleryFilters = GalleryFilterOption.CreateAll();
        _selectedGalleryFilter = GalleryFilters[0];
        _selectedTagFilter = new TagFilterOption(null, AppText.AllTags);
        TagFilters.Add(_selectedTagFilter);
        _selectedDatePeriod = DatePeriods[0];
        _selectedSortOrder = SortOrders[0];
        ClearDateFilterCommand = new RelayCommand(_ => ClearDateFilter());
        _analyzeCommand = new AsyncRelayCommand(
            _ => AnalyzeDuplicatesAsync(),
            _ => CanAnalyze);
        _cancelAnalysisCommand = new RelayCommand(
            _ => CancelAnalysis(),
            _ => IsAnalyzing);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
        _refreshTimer.Tick += RefreshTimerOnTick;
        _searchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchTimer.Tick += SearchTimerOnTick;
    }

    public ObservableCollection<FolderViewModel> Folders { get; } = new();
    public IReadOnlyList<FolderViewModel> VisibleFolders
    {
        get => _visibleFolders;
        private set => SetProperty(ref _visibleFolders, value);
    }

    public ObservableCollection<ScreenshotItemViewModel> VisibleItems { get; } = new();
    public ObservableCollection<TagFilterOption> TagFilters { get; } = new();
    public IReadOnlyList<GalleryFilterOption> GalleryFilters { get; }
    public IReadOnlyList<BrowseOption> DatePeriods { get; } = BrowseOption.DatePeriods();
    public IReadOnlyList<BrowseOption> SortOrders { get; } = BrowseOption.SortOrders();
    public ICommand ClearDateFilterCommand { get; }
    public IReadOnlyList<int> RefreshIntervals { get; } = [1, 5, 10, 30, 60];

    public ICommand ScanCommand => _scanCommand;
    public ICommand CancelScanCommand => _cancelScanCommand;
    public ICommand PreviousPageCommand => _previousPageCommand;
    public ICommand NextPageCommand => _nextPageCommand;
    public ICommand RemoveRootCommand => _removeRootCommand;
    public ICommand AnalyzeCommand => _analyzeCommand;
    public ICommand CancelAnalysisCommand => _cancelAnalysisCommand;

    public FolderViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                OnPropertyChanged(nameof(CollectionTitle));
                OnPropertyChanged(nameof(CollectionSubtitle));
                if (!_isRebuildingFolders)
                {
                    ApplyFilter(resetPage: true);
                }
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
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }
    }

    public GalleryFilterOption SelectedGalleryFilter
    {
        get => _selectedGalleryFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedGalleryFilter, value))
            {
                return;
            }

            if (!_isApplyingSettings)
            {
                _settings.GalleryFilter = ToSettingsKey(value.Kind);
                _ = SaveSettingsQuietlyAsync();
            }

            ApplyFilter(resetPage: true);
        }
    }

    public TagFilterOption SelectedTagFilter
    {
        get => _selectedTagFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedTagFilter, value))
            {
                return;
            }

            if (!_isApplyingSettings)
            {
                _settings.TagFilter = value.Tag;
                _ = SaveSettingsQuietlyAsync();
            }

            ApplyFilter(resetPage: true);
        }
    }

    public BrowseOption SelectedDatePeriod
    {
        get => _selectedDatePeriod;
        set
        {
            if (value is null || !SetProperty(ref _selectedDatePeriod, value)) return;
            OnPropertyChanged(nameof(IsCustomDatePeriod));
            OnPropertyChanged(nameof(HasDateFilter));
            BrowseOptionsChanged();
        }
    }

    public BrowseOption SelectedSortOrder
    {
        get => _selectedSortOrder;
        set
        {
            if (value is null || !SetProperty(ref _selectedSortOrder, value)) return;
            BrowseOptionsChanged();
        }
    }

    public DateTime? DateFrom
    {
        get => _dateFrom;
        set
        {
            if (SetProperty(ref _dateFrom, value?.Date)) BrowseOptionsChanged();
        }
    }

    public DateTime? DateTo
    {
        get => _dateTo;
        set
        {
            if (SetProperty(ref _dateTo, value?.Date)) BrowseOptionsChanged();
        }
    }

    public bool IsCustomDatePeriod => SelectedDatePeriod.Key == "custom";
    public bool HasDateFilter => SelectedDatePeriod.Key != "all-time";
    public bool HasInvalidDateRange => IsCustomDatePeriod && DateFrom > DateTo;

    private void BrowseOptionsChanged()
    {
        OnPropertyChanged(nameof(HasInvalidDateRange));
        if (_isApplyingSettings) return;

        _settings.DatePeriod = SelectedDatePeriod.Key;
        _settings.SortOrder = SelectedSortOrder.Key;
        _settings.DateFrom = DateFrom;
        _settings.DateTo = DateTo;
        _ = SaveSettingsQuietlyAsync();
        ApplyFilter(resetPage: true);
    }

    private void ClearDateFilter()
    {
        _dateFrom = null;
        _dateTo = null;
        _selectedDatePeriod = DatePeriods[0];
        OnPropertyChanged(nameof(DateFrom));
        OnPropertyChanged(nameof(DateTo));
        OnPropertyChanged(nameof(SelectedDatePeriod));
        OnPropertyChanged(nameof(IsCustomDatePeriod));
        OnPropertyChanged(nameof(HasDateFilter));
        BrowseOptionsChanged();
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
            OnPropertyChanged(nameof(IsFolderOnlyEmpty));
            OnPropertyChanged(nameof(CanEditFolders));
            OnPropertyChanged(nameof(CanAnalyze));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(LoadingTitle));
            OnPropertyChanged(nameof(LoadingDetail));
            _scanCommand.RaiseCanExecuteChanged();
            _cancelScanCommand.RaiseCanExecuteChanged();
            _removeRootCommand.RaiseCanExecuteChanged();
            _analyzeCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(ref _isAnalyzing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanAnalyze));
            OnPropertyChanged(nameof(CanEditFolders));
            OnPropertyChanged(nameof(IsBusy));
            _scanCommand.RaiseCanExecuteChanged();
            _removeRootCommand.RaiseCanExecuteChanged();
            _analyzeCommand.RaiseCanExecuteChanged();
            _cancelAnalysisCommand.RaiseCanExecuteChanged();
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

    public bool FolderOnlyMode
    {
        get => _folderOnlyMode;
        set
        {
            if (!SetProperty(ref _folderOnlyMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsGalleryMode));
            OnPropertyChanged(nameof(IsFolderOnlyMode));
            OnPropertyChanged(nameof(CollectionTitle));
            OnPropertyChanged(nameof(SearchPrompt));
            OnPropertyChanged(nameof(SearchTooltip));
            if (Folders.FirstOrDefault() is { IsAll: true } allFolder)
            {
                allFolder.DisplayName = value ? AppText.AllDetectedFolders : AppText.AllScreenshots;
            }

            if (!_isApplyingSettings)
            {
                _settings.FolderOnlyMode = value;
                _ = SaveSettingsQuietlyAsync();
            }

            ApplyFilter(resetPage: true);
            if (!value && _isInitialized && !_imageCatalogLoaded)
            {
                _ = EnsureGalleryCatalogLoadedAsync();
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
    public bool IsLoading => (IsScanning && _records.Count == 0) || _isRestoringCatalog;
    public bool CanEditFolders => !IsScanning && !IsAnalyzing && !_isRestoringCatalog;
    public bool CanAnalyze => _imageCatalogLoaded && _records.Count > 0 &&
                              !IsScanning && !IsAnalyzing && !_isRestoringCatalog;
    public bool IsBusy => IsScanning || IsAnalyzing || _isRestoringCatalog;
    public bool HasVisibleItems => VisibleItems.Count > 0;
    public bool IsEmpty => IsGalleryMode && !IsScanning && !IsLoading && VisibleItems.Count == 0;
    public bool IsFolderOnlyEmpty => IsFolderOnlyMode && !IsScanning && !IsLoading && VisibleFolders.Count == 0;
    public bool HasMultiplePages => IsGalleryMode && PageCount > 1;
    public bool IsGalleryMode => !FolderOnlyMode;
    public bool IsFolderOnlyMode => FolderOnlyMode;
    public string LoadingTitle => _isRestoringCatalog ? AppText.LoadingSavedList : AppText.SearchingScreenshots;
    public string LoadingDetail => _isRestoringCatalog ? AppText.LoadingSavedListDetail : AppText.SearchingDeepLocations;

    public string CollectionTitle => FolderOnlyMode
        ? SelectedFolder is { IsAll: false } folder
            ? folder.DisplayName
            : AppText.FolderOnlyTitle
        : SelectedFolder?.DisplayName ?? AppText.AllScreenshots;

    public string CollectionSubtitle => FolderOnlyMode
        ? AppText.FolderSummary(VisibleFolders.Count)
        : _filteredRecords.Count == 0
            ? AppText.NoScreenshotsYet
            : AppText.CollectionSummary(_filteredRecords.Count, SelectedSortOrder.DisplayName);

    public string SearchPrompt => FolderOnlyMode ? AppText.SearchFoldersPrompt : AppText.SearchPrompt;

    public string SearchTooltip => FolderOnlyMode ? AppText.SearchFoldersTooltip : AppText.SearchTooltip;

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

    public string EmptyTitle => !_imageCatalogLoaded && _knownScreenshotCount > 0
        ? AppText.GalleryListUnavailable
        : _records.Count switch
        {
            0 => AppText.EmptyNotFound,
            _ when !string.IsNullOrWhiteSpace(SearchText) || HasDateFilter ||
                   SelectedGalleryFilter.Kind != GalleryFilterKind.All ||
                   SelectedTagFilter.Tag is not null => AppText.EmptySearch,
            _ => AppText.EmptyCollection
        };

    public string EmptyDetail => !_imageCatalogLoaded && _knownScreenshotCount > 0
        ? AppText.GalleryListUnavailableHint
        : _records.Count == 0
            ? AppText.EmptyInitialHint
            : AppText.EmptyFilteredHint;

    public string FolderEmptyTitle => string.IsNullOrWhiteSpace(SearchText)
        ? AppText.EmptyFoldersNotFound
        : AppText.EmptyFolderSearch;

    public string FolderEmptyDetail => string.IsNullOrWhiteSpace(SearchText)
        ? AppText.EmptyFoldersHint
        : AppText.EmptyFolderSearchHint;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        SetRestoringCatalog(true);
        try
        {
            _settings = await _settingsStore.LoadAsync();
        }
        catch (Exception exception)
        {
            _settings = HubSettings.CreateDefault();
            StatusText = AppText.SettingsLoadFailed;
            StatusDetail = exception.Message;
        }

        await LoadAuxiliaryDataAsync();

        _isApplyingSettings = true;
        FolderOnlyMode = _settings.FolderOnlyMode;
        AutoRefresh = _settings.AutoRefresh;
        AutoRefreshMinutes = _settings.AutoRefreshMinutes;
        SelectedGalleryFilter = GalleryFilters.First(option =>
            option.Kind == FromSettingsKey(_settings.GalleryFilter));
        SelectedTagFilter = TagFilters.FirstOrDefault(option =>
            string.Equals(option.Tag, _settings.TagFilter, StringComparison.CurrentCultureIgnoreCase)) ??
            TagFilters[0];
        SelectedDatePeriod = DatePeriods.First(option => option.Key == _settings.DatePeriod);
        SelectedSortOrder = SortOrders.First(option => option.Key == _settings.SortOrder);
        DateFrom = _settings.DateFrom;
        DateTo = _settings.DateTo;
        _isApplyingSettings = false;
        if (_restrictToSessionRoots)
        {
            SetRestoringCatalog(false);
            await ScanAsync();
            return;
        }

        var scanRequired = false;
        try
        {
            var folderCatalog = await _folderCatalogStore.LoadAsync();
            _folderCatalogScanUtc = folderCatalog?.LastSuccessfulScanUtc;
            if (FolderOnlyMode && folderCatalog is not null)
            {
                ApplyFolderCatalog(folderCatalog);
            }
            else
            {
                var catalog = await _catalogStore.LoadAsync();
                if (catalog is not null)
                {
                    ApplyCatalog(catalog);
                    if (_folderCatalogScanUtc is null ||
                        _folderCatalogScanUtc.Value < catalog.LastSuccessfulScanUtc)
                    {
                        await SaveFolderCatalogQuietlyAsync(catalog);
                    }

                    if (_folderCatalogScanUtc is { } folderScanUtc &&
                        folderScanUtc > catalog.LastSuccessfulScanUtc)
                    {
                        StatusDetail = AppText.GalleryListOlderThanFolders;
                    }
                }
                else if (folderCatalog is not null)
                {
                    ApplyFolderCatalog(folderCatalog);
                    StatusText = AppText.SavedFoldersLoaded(folderCatalog.Folders.Count);
                    StatusDetail = AppText.UseLightweightOrRescan;
                }
                else
                {
                    _hasPersistedCatalog = false;
                    scanRequired = true;
                }
            }
        }
        finally
        {
            SetRestoringCatalog(false);
        }

        if (!scanRequired)
        {
            ConfigureRefreshTimer();
            return;
        }

        await ScanAsync();
    }

    public async Task AddCustomRootAsync(string folderPath)
    {
        if (IsScanning || IsAnalyzing || _isRestoringCatalog)
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
        ReconcileCurrentViewWithSettings();
        await ScanAsync();
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    public async Task ToggleFavoriteAsync(string filePath)
    {
        var normalized = NormalizePath(filePath);
        if (normalized is null)
        {
            return;
        }

        _userData.TryGetValue(normalized, out var current);
        var updated = new ScreenshotUserData
        {
            FilePath = normalized,
            IsFavorite = current?.IsFavorite != true,
            Tags = current?.Tags?.ToList() ?? [],
            UpdatedUtc = DateTime.UtcNow
        };
        SetUserData(updated);
        await SaveUserDataQuietlyAsync();
        RebuildTagFilters();
        ApplyFilter(resetPage: false);
    }

    public async Task UpdateTagsAsync(string filePath, IEnumerable<string> tags)
    {
        var normalized = NormalizePath(filePath);
        if (normalized is null)
        {
            return;
        }

        _userData.TryGetValue(normalized, out var current);
        var normalizedTags = tags
            .Select(NormalizeTag)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(32)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SetUserData(new ScreenshotUserData
        {
            FilePath = normalized,
            IsFavorite = current?.IsFavorite == true,
            Tags = normalizedTags,
            UpdatedUtc = DateTime.UtcNow
        });
        await SaveUserDataQuietlyAsync();
        RebuildTagFilters();
        ApplyFilter(resetPage: false);
    }

    public IReadOnlyList<ScreenshotRecord> GetViewerRecords()
        => _filteredRecords.ToArray();

    public ScreenshotUserData GetUserDataSnapshot(string filePath)
    {
        var normalized = NormalizePath(filePath);
        if (normalized is not null && _userData.TryGetValue(normalized, out var item))
        {
            return new ScreenshotUserData
            {
                FilePath = item.FilePath,
                IsFavorite = item.IsFavorite,
                Tags = item.Tags.ToList(),
                UpdatedUtc = item.UpdatedUtc
            };
        }

        return new ScreenshotUserData { FilePath = normalized ?? filePath };
    }

    public DuplicateMembership GetDuplicateMembership(string filePath)
        => _duplicateMemberships.TryGetValue(filePath, out var membership)
            ? membership
            : new DuplicateMembership(null, 0, null, 0);

    private async Task LoadAuxiliaryDataAsync()
    {
        try
        {
            var catalog = await _userDataStore.LoadAsync();
            _userData.Clear();
            foreach (var item in catalog.Items)
            {
                _userData[item.FilePath] = item;
            }
        }
        catch (Exception exception)
        {
            StatusText = AppText.SettingsLoadFailed;
            StatusDetail = exception.Message;
        }

        try
        {
            var catalog = await _analysisStore.LoadAsync();
            _analyses.Clear();
            foreach (var item in catalog.Items)
            {
                _analyses[item.FilePath] = item;
            }
        }
        catch (Exception exception)
        {
            StatusText = AppText.AnalysisFailed;
            StatusDetail = exception.Message;
        }

        RebuildTagFilters(_settings.TagFilter);
    }

    private async Task AnalyzeDuplicatesAsync()
    {
        if (!CanAnalyze)
        {
            return;
        }

        _refreshTimer.Stop();
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;
        IsAnalyzing = true;
        StatusText = AppText.AnalysisPreparing;
        StatusDetail = AppText.AnalyzeImagesTooltip;

        var progress = new Progress<ImageAnalysisProgress>(value =>
        {
            if (value.Completed == 1 || value.Completed == value.Total || value.Completed % 10 == 0)
            {
                StatusText = AppText.AnalysisProgress(value.Completed, value.Total);
                StatusDetail = ShortenPath(value.CurrentPath);
            }
        });

        try
        {
            var results = await _imageAnalysisService.AnalyzeAsync(
                _records.ToArray(),
                _analyses.Values.ToArray(),
                progress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _analyses.Clear();
            foreach (var item in results)
            {
                _analyses[item.FilePath] = item;
            }

            var completedUtc = DateTime.UtcNow;
            await _analysisStore.SaveAsync(
                ScreenshotAnalysisStore.Create(results, completedUtc),
                cancellationToken);
            RebuildDuplicateMemberships();
            ApplyFilter(resetPage: false);
            var exactGroups = _duplicateMemberships.Values
                .Where(item => item.IsExactDuplicate)
                .Select(item => item.ExactGroupId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var similarGroups = _duplicateMemberships.Values
                .Where(item => item.IsSimilar)
                .Select(item => item.SimilarGroupId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            StatusText = AppText.AnalysisComplete(exactGroups, similarGroups);
            StatusDetail = AppText.AnalyzeImagesTooltip;
        }
        catch (OperationCanceledException)
        {
            StatusText = AppText.AnalysisCancelled;
            StatusDetail = AppText.ListNotUpdated;
        }
        catch (Exception exception)
        {
            StatusText = AppText.AnalysisFailed;
            StatusDetail = exception.Message;
        }
        finally
        {
            IsAnalyzing = false;
            ConfigureRefreshTimer();
        }
    }

    private void CancelAnalysis() => _analysisCancellation?.Cancel();

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
            var result = await ScreenshotScanner.ScanIncrementalAsync(
                roots,
                Math.Clamp(_settings.MaxDepth, 1, 64),
                _records.ToArray(),
                progress,
                cancellationToken);

            var completedUtc = DateTime.UtcNow;
            cancellationToken.ThrowIfCancellationRequested();
            var keepPreviousCatalog = !_restrictToSessionRoots &&
                                      result.Warnings.Count > 0 &&
                                      _hasPersistedCatalog;
            if (keepPreviousCatalog)
            {
                ReconcileCurrentViewWithSettings();
                StatusText = AppText.ScreenshotsFound(_knownScreenshotCount);
                StatusDetail = AppText.CatalogKeptAfterWarnings(result.Warnings.Count);
            }
            else
            {
                _records.Clear();
                var ignoredRoots = GetIgnoredPaths();
                _records.AddRange(result.Screenshots
                    .Where(record => !ignoredRoots.Any(root => IsSameOrDescendant(record.FilePath, root)))
                    .OrderByDescending(record => record.LastWriteTimeUtc)
                    .ThenBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase));
                _knownScreenshotCount = _records.Count;
                _catalogScanUtc = completedUtc;
                _imageCatalogLoaded = true;
                RebuildFolders(_records, result.ScannedRoots);
                RebuildDuplicateMemberships();
                ApplyFilter(resetPage: true);

                LastUpdatedText = AppText.LastScanned(completedUtc.ToLocalTime());
                StatusText = AppText.ScreenshotsFound(_records.Count);
                StatusDetail = result.Warnings.Count == 0
                    ? AppText.IncrementalScanCompleted(
                        result.DirectoriesVisited,
                        result.Incremental.Reused,
                        result.Incremental.AddedOrUpdated,
                        result.Incremental.Removed,
                        FormatDuration(result.Duration))
                    : AppText.CompletedWithWarnings(result.Warnings.Count);

                if (!_restrictToSessionRoots)
                {
                    Exception? catalogSaveError = null;
                    try
                    {
                        await _folderCatalogStore.SaveAsync(
                            ScreenshotFolderCatalogStore.Create(
                                _records,
                                result.ScannedRoots,
                                completedUtc),
                            cancellationToken);
                        _folderCatalogScanUtc = completedUtc;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        catalogSaveError = exception;
                    }

                    if (catalogSaveError is null)
                    {
                        try
                        {
                            var imageCatalog = ScreenshotCatalogStore.Create(
                                _records,
                                result.ScannedRoots,
                                completedUtc);
                            await _catalogStore.SaveAsync(
                                imageCatalog,
                                cancellationToken);
                            _hasPersistedCatalog = true;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            catalogSaveError = exception;
                        }
                    }

                    if (catalogSaveError is not null)
                    {
                        StatusText = AppText.CatalogSaveFailed;
                        StatusDetail = catalogSaveError.Message;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = AppText.ScanCancelled;
            StatusDetail = _knownScreenshotCount == 0
                ? AppText.ListNotUpdated
                : AppText.PreviousImages(_knownScreenshotCount);
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

    private void ApplyCatalog(ScreenshotCatalog catalog)
    {
        var ignoredPaths = GetIgnoredPaths();
        _records.Clear();
        _records.AddRange(catalog.Screenshots
            .Where(record => !ignoredPaths.Any(root => IsSameOrDescendant(record.FilePath, root))));
        var roots = ReconcileCatalogRoots(catalog.Roots, ignoredPaths);
        _knownScreenshotCount = _records.Count;
        _catalogScanUtc = catalog.LastSuccessfulScanUtc;
        _imageCatalogLoaded = true;
        _hasPersistedCatalog = true;
        RebuildFolders(_records, roots);
        RebuildDuplicateMemberships();
        ApplyFilter(resetPage: true);
        LastUpdatedText = AppText.LastScanned(catalog.LastSuccessfulScanUtc.ToLocalTime());
        StatusText = AppText.SavedCatalogLoaded(_records.Count);
        StatusDetail = AppText.RescanToUpdate;
    }

    private void ApplyFolderCatalog(ScreenshotFolderCatalog catalog)
    {
        var selectedPath = SelectedFolder?.Path;
        var ignoredPaths = GetIgnoredPaths();
        var customRoots = _settings.CustomRoots
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Where(path => !ignoredPaths.Any(ignored => IsSameOrDescendant(path, ignored)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var folders = catalog.Folders
            .Where(entry => !ignoredPaths.Any(root => IsSameOrDescendant(entry.Path, root)))
            .Select(entry =>
            {
                var customRoot = customRoots
                    .OrderByDescending(path => path.Length)
                    .FirstOrDefault(path => IsSameOrDescendant(entry.Path, path));
                return new FolderViewModel(entry.DisplayName, entry.Path, customRootPath: customRoot)
                {
                    Count = entry.Count,
                    LatestUtc = entry.LatestUtc
                };
            })
            .ToList();

        foreach (var customRoot in customRoots)
        {
            if (folders.Any(folder =>
                    folder.Path is not null && IsSameOrDescendant(folder.Path, customRoot)))
            {
                continue;
            }

            folders.Add(new FolderViewModel(
                GetFolderName(customRoot),
                customRoot,
                customRootPath: customRoot));
        }

        var totalScreenshots = folders.Sum(folder => (long)folder.Count);
        var allFolder = new FolderViewModel(
            FolderOnlyMode ? AppText.AllDetectedFolders : AppText.AllScreenshots,
            null,
            isAll: true)
        {
            Count = (int)Math.Min(totalScreenshots, int.MaxValue),
            LatestUtc = folders.Count > 0 ? folders.Max(folder => folder.LatestUtc) : null
        };

        Folders.Clear();
        Folders.Add(allFolder);
        foreach (var folder in folders
                     .OrderByDescending(folder => folder.LatestUtc ?? DateTime.MinValue)
                     .ThenBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Folders.Add(folder);
        }

        _isRebuildingFolders = true;
        try
        {
            SelectedFolder = selectedPath is null
                ? allFolder
                : Folders.FirstOrDefault(folder =>
                    string.Equals(folder.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? allFolder;
        }
        finally
        {
            _isRebuildingFolders = false;
        }

        _records.Clear();
        _filteredRecords.Clear();
        _knownScreenshotCount = allFolder.Count;
        _catalogScanUtc = catalog.LastSuccessfulScanUtc;
        _folderCatalogScanUtc = catalog.LastSuccessfulScanUtc;
        _imageCatalogLoaded = false;
        _hasPersistedCatalog = true;
        ApplyFilter(resetPage: true);
        LastUpdatedText = AppText.LastScanned(catalog.LastSuccessfulScanUtc.ToLocalTime());
        StatusText = AppText.SavedCatalogLoaded(_knownScreenshotCount);
        StatusDetail = AppText.RescanToUpdate;
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

    private string[] GetIgnoredPaths()
        => _settings.IgnoredRoots
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<ScanRoot> ReconcileCatalogRoots(
        IEnumerable<CatalogRoot> catalogRoots,
        IReadOnlyList<string> ignoredPaths)
    {
        var roots = new Dictionary<string, ScanRoot>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in catalogRoots)
        {
            var path = NormalizePath(root.Path);
            if (path is null || ignoredPaths.Any(ignored => IsSameOrDescendant(path, ignored)))
            {
                continue;
            }

            roots[path] = new ScanRoot(path, root.DisplayName, root.IsCustom)
            {
                GameNameHint = root.GameNameHint,
                IgnoredPaths = ignoredPaths
            };
        }

        foreach (var customRootValue in _settings.CustomRoots)
        {
            var path = NormalizePath(customRootValue);
            if (path is null || ignoredPaths.Any(ignored => IsSameOrDescendant(path, ignored)))
            {
                continue;
            }

            if (roots.TryGetValue(path, out var existing))
            {
                roots[path] = existing with { IsCustom = true };
            }
            else
            {
                roots[path] = new ScanRoot(path, GetFolderName(path), true)
                {
                    IgnoredPaths = ignoredPaths
                };
            }
        }

        return roots.Values.ToArray();
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
                var matchingRoot = scannedRoots
                    .OrderByDescending(root => root.Path.Length)
                    .FirstOrDefault(root => IsSameOrDescendant(group.Key, root.Path));
                var customRoot = customRoots
                    .OrderByDescending(path => path.Length)
                    .FirstOrDefault(path => IsSameOrDescendant(group.Key, path));
                var recordName = group
                    .Select(record => record.GameName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? GetFolderName(group.Key);
                var displayName = !string.IsNullOrWhiteSpace(matchingRoot?.GameNameHint)
                    ? matchingRoot.GameNameHint!
                    : matchingRoot is not null &&
                      string.Equals(group.Key, matchingRoot.Path, StringComparison.OrdinalIgnoreCase)
                        ? matchingRoot.DisplayName
                        : recordName;

                return new FolderViewModel(displayName, group.Key, customRootPath: customRoot)
                {
                    Count = group.Count(),
                    LatestUtc = group.Max(record => record.LastWriteTimeUtc)
                };
            })
            .ToList();

        foreach (var root in scannedRoots.Where(root => root.IsCustom))
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

        var allFolder = new FolderViewModel(
            FolderOnlyMode ? AppText.AllDetectedFolders : AppText.AllScreenshots,
            null,
            isAll: true)
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

        _isRebuildingFolders = true;
        try
        {
            SelectedFolder = selectedPath is null
                ? allFolder
                : Folders.FirstOrDefault(folder =>
                    string.Equals(folder.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? allFolder;
        }
        finally
        {
            _isRebuildingFolders = false;
        }
    }

    private void ApplyFilter(bool resetPage)
    {
        if (resetPage)
        {
            PageIndex = 0;
        }

        var query = SearchText.Trim();

        if (FolderOnlyMode)
        {
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _thumbnailCancellation = null;
            _thumbnailService.Clear();
            VisibleItems.Clear();
            var selectedPath = SelectedFolder?.Path;
            VisibleFolders = Folders.Where(folder =>
                    !folder.IsAll &&
                    (string.IsNullOrWhiteSpace(selectedPath) ||
                     string.Equals(folder.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) &&
                    (query.Length == 0 ||
                     folder.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                     (folder.Path?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)))
                .ToArray();

            _filteredRecords.Clear();
            NotifyGalleryStateChanged();
            return;
        }

        VisibleFolders = [];
        var folderPath = SelectedFolder?.Path;

        _filteredRecords.Clear();
        var matchingRecords = _records.Where(record =>
        {
            if (!string.IsNullOrWhiteSpace(folderPath) &&
                !string.Equals(record.LibraryFolder, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _userData.TryGetValue(record.FilePath, out var userData);
            _duplicateMemberships.TryGetValue(record.FilePath, out var membership);
            var matchesGalleryFilter = SelectedGalleryFilter.Kind switch
            {
                GalleryFilterKind.Favorites => userData?.IsFavorite == true,
                GalleryFilterKind.Tagged => userData?.Tags.Count > 0,
                GalleryFilterKind.Untagged => userData?.Tags.Count is null or 0,
                GalleryFilterKind.ExactDuplicates => membership?.IsExactDuplicate == true,
                GalleryFilterKind.SimilarImages => membership?.IsSimilar == true,
                _ => true
            };
            if (!matchesGalleryFilter)
            {
                return false;
            }

            if (SelectedTagFilter.Tag is { Length: > 0 } tag &&
                userData?.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase) != true)
            {
                return false;
            }

            return query.Length == 0 ||
                   record.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   record.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   (userData?.Tags.Any(tagValue =>
                       tagValue.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ?? false);
        });
        matchingRecords = ScreenshotBrowseQuery.FilterByDate(
            matchingRecords, SelectedDatePeriod.Key, DateFrom, DateTo, DateTime.Today);
        Func<ScreenshotRecord, string?>? groupKey = SelectedGalleryFilter.Kind switch
        {
            GalleryFilterKind.ExactDuplicates => record => _duplicateMemberships[record.FilePath].ExactGroupId,
            GalleryFilterKind.SimilarImages => record => _duplicateMemberships[record.FilePath].SimilarGroupId,
            _ => null
        };
        _filteredRecords.AddRange(ScreenshotBrowseQuery.Sort(matchingRecords, SelectedSortOrder.Key, groupKey));

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
            _userData.TryGetValue(record.FilePath, out var userData);
            _duplicateMemberships.TryGetValue(record.FilePath, out var membership);
            VisibleItems.Add(new ScreenshotItemViewModel(record, userData, membership));
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

    private void SearchTimerOnTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplyFilter(resetPage: true);
    }

    private async Task EnsureGalleryCatalogLoadedAsync()
    {
        if (_imageCatalogLoaded || _isRestoringCatalog || FolderOnlyMode)
        {
            return;
        }

        _refreshTimer.Stop();
        SetRestoringCatalog(true);
        var scanRequired = false;
        try
        {
            var catalog = await _catalogStore.LoadAsync();
            if (catalog is not null)
            {
                if (!FolderOnlyMode)
                {
                    ApplyCatalog(catalog);
                    if (_folderCatalogScanUtc is { } folderScanUtc &&
                        folderScanUtc > catalog.LastSuccessfulScanUtc)
                    {
                        StatusDetail = AppText.GalleryListOlderThanFolders;
                    }
                }
            }
            else if (_folderCatalogScanUtc is not null || Folders.Count > 0)
            {
                StatusText = AppText.SavedFoldersLoaded(
                    Folders.Count(folder => !folder.IsAll));
                StatusDetail = AppText.UseLightweightOrRescan;
            }
            else
            {
                _hasPersistedCatalog = false;
                scanRequired = !FolderOnlyMode;
            }
        }
        catch (Exception exception)
        {
            StatusText = AppText.ScanFailed;
            StatusDetail = exception.Message;
        }
        finally
        {
            SetRestoringCatalog(false);
            ConfigureRefreshTimer();
        }

        if (scanRequired && !FolderOnlyMode)
        {
            await ScanAsync();
        }
    }

    private async Task SaveFolderCatalogQuietlyAsync(ScreenshotCatalog catalog)
    {
        try
        {
            var ignoredPaths = GetIgnoredPaths();
            var roots = ReconcileCatalogRoots(catalog.Roots, ignoredPaths);
            await _folderCatalogStore.SaveAsync(
                ScreenshotFolderCatalogStore.Create(
                    _records,
                    roots,
                    catalog.LastSuccessfulScanUtc));
            _folderCatalogScanUtc = catalog.LastSuccessfulScanUtc;
        }
        catch (Exception exception)
        {
            StatusText = AppText.CatalogSaveFailed;
            StatusDetail = exception.Message;
        }
    }

    private async Task RemoveCustomRootAsync(FolderViewModel folder)
    {
        if (_isRestoringCatalog || IsScanning || IsAnalyzing)
        {
            return;
        }

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
        ReconcileCurrentViewWithSettings();
        await ScanAsync();
    }

    private void ReconcileCurrentViewWithSettings()
    {
        if (_catalogScanUtc == default || Folders.Count == 0)
        {
            return;
        }

        if (!_imageCatalogLoaded)
        {
            var entries = Folders
                .Where(folder => !folder.IsAll && folder.Path is not null)
                .Select(folder => new FolderCatalogEntry(
                    folder.Path!,
                    folder.DisplayName,
                    folder.CanRemove,
                    folder.Count,
                    folder.LatestUtc))
                .ToList();
            ApplyFolderCatalog(new ScreenshotFolderCatalog
            {
                FormatVersion = ScreenshotFolderCatalogStore.CurrentFormatVersion,
                LastSuccessfulScanUtc = _catalogScanUtc,
                Folders = entries,
                TotalScreenshots = (int)Math.Min(
                    entries.Sum(entry => (long)entry.Count),
                    int.MaxValue)
            });
            return;
        }

        var ignoredPaths = GetIgnoredPaths();
        _records.RemoveAll(record =>
            ignoredPaths.Any(root => IsSameOrDescendant(record.FilePath, root)));
        var cachedRoots = Folders
            .Where(folder => !folder.IsAll && folder.Path is not null)
            .Select(folder => new CatalogRoot(
                folder.Path!,
                folder.DisplayName,
                folder.CanRemove,
                null));
        var roots = ReconcileCatalogRoots(cachedRoots, ignoredPaths);
        _knownScreenshotCount = _records.Count;
        RebuildFolders(_records, roots);
        ApplyFilter(resetPage: true);
    }

    private void SetRestoringCatalog(bool value)
    {
        if (_isRestoringCatalog == value)
        {
            return;
        }

        _isRestoringCatalog = value;
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFolderOnlyEmpty));
        OnPropertyChanged(nameof(CanEditFolders));
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(LoadingTitle));
        OnPropertyChanged(nameof(LoadingDetail));
        _scanCommand.RaiseCanExecuteChanged();
        _removeRootCommand.RaiseCanExecuteChanged();
        _analyzeCommand.RaiseCanExecuteChanged();
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

    private async Task SaveUserDataQuietlyAsync()
    {
        try
        {
            await _userDataStore.SaveAsync(ScreenshotUserDataStore.Create(_userData.Values));
        }
        catch (Exception exception)
        {
            StatusText = AppText.SettingsSaveFailed;
            StatusDetail = exception.Message;
        }
    }

    private void SetUserData(ScreenshotUserData item)
    {
        if (!item.IsFavorite && item.Tags.Count == 0)
        {
            _userData.Remove(item.FilePath);
            return;
        }

        _userData[item.FilePath] = item;
    }

    private void RebuildTagFilters(string? preferredTag = null)
    {
        var selectedTag = preferredTag ?? _selectedTagFilter.Tag;
        var tags = _userData.Values
            .SelectMany(item => item.Tags)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        TagFilters.Clear();
        TagFilters.Add(new TagFilterOption(null, AppText.AllTags));
        foreach (var tag in tags)
        {
            TagFilters.Add(new TagFilterOption(tag, tag));
        }

        _selectedTagFilter = TagFilters.FirstOrDefault(option =>
            string.Equals(option.Tag, selectedTag, StringComparison.CurrentCultureIgnoreCase)) ?? TagFilters[0];
        OnPropertyChanged(nameof(SelectedTagFilter));
    }

    private void RebuildDuplicateMemberships()
    {
        var current = _records.ToDictionary(
            record => record.FilePath,
            record => record,
            StringComparer.OrdinalIgnoreCase);
        var validAnalyses = _analyses.Values.Where(analysis =>
            current.TryGetValue(analysis.FilePath, out var screenshot) &&
            screenshot.FileSize == analysis.FileSize &&
            screenshot.LastWriteTimeUtc == analysis.LastWriteTimeUtc).ToArray();
        _duplicateMemberships = DuplicateGrouper.Build(validAnalyses);
    }

    private static string NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var value = new string(tag.Trim().Where(character => !char.IsControl(character)).ToArray());
        return value.Length <= 64 ? value : value[..64];
    }

    private static string ToSettingsKey(GalleryFilterKind kind) => kind switch
    {
        GalleryFilterKind.Favorites => "favorites",
        GalleryFilterKind.Tagged => "tagged",
        GalleryFilterKind.Untagged => "untagged",
        GalleryFilterKind.ExactDuplicates => "exact-duplicates",
        GalleryFilterKind.SimilarImages => "similar-images",
        _ => "all"
    };

    private static GalleryFilterKind FromSettingsKey(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "favorites" => GalleryFilterKind.Favorites,
        "tagged" => GalleryFilterKind.Tagged,
        "untagged" => GalleryFilterKind.Untagged,
        "exact-duplicates" => GalleryFilterKind.ExactDuplicates,
        "similar-images" => GalleryFilterKind.SimilarImages,
        _ => GalleryFilterKind.All
    };

    private void ConfigureRefreshTimer()
    {
        _refreshTimer.Stop();
        if (!AutoRefresh || IsScanning || IsAnalyzing || _isRestoringCatalog || !_isInitialized)
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
        OnPropertyChanged(nameof(IsFolderOnlyEmpty));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(CollectionSubtitle));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        OnPropertyChanged(nameof(FolderEmptyTitle));
        OnPropertyChanged(nameof(FolderEmptyDetail));
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
        _searchTimer.Stop();
        _searchTimer.Tick -= SearchTimerOnTick;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
    }
}
