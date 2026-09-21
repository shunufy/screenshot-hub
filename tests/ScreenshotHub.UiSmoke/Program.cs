using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenshotHub;
using ScreenshotHub.Core;
using ScreenshotHub.ViewModels;

namespace ScreenshotHub.UiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outputDirectory = Path.GetFullPath(args.FirstOrDefault() ??
            Path.Combine(AppContext.BaseDirectory, "ui-smoke-output"));
        Directory.CreateDirectory(outputDirectory);
        var fixtureDirectory = Path.Combine(outputDirectory, "fixture", "Screenshots");
        var dataDirectory = Path.Combine(outputDirectory, "data");
        Directory.CreateDirectory(fixtureDirectory);
        Directory.CreateDirectory(dataDirectory);

        var firstPath = Path.Combine(fixtureDirectory, "夜景-001.png");
        var exactCopyPath = Path.Combine(fixtureDirectory, "夜景-001-copy.png");
        var differentPath = Path.Combine(fixtureDirectory, "戦闘-002.png");
        var exactBytes = CreatePng(width: 640, height: 360, variant: 0);
        File.WriteAllBytes(firstPath, exactBytes);
        File.WriteAllBytes(exactCopyPath, exactBytes);
        File.WriteAllBytes(differentPath, CreatePng(width: 640, height: 360, variant: 1));
        var fixedUtc = DateTime.Today.AddHours(12).ToUniversalTime();
        foreach (var path in new[] { firstPath, exactCopyPath, differentPath })
        {
            File.SetLastWriteTimeUtc(path, fixedUtc);
        }
        File.SetLastWriteTimeUtc(differentPath, fixedUtc.AddDays(-10));

        var beforeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstPath)));
        var beforeTimestamp = File.GetLastWriteTimeUtc(firstPath);
        try
        {
            App.SuppressAutomaticStartupForUiSmoke = true;
            var application = new App();
            application.InitializeComponent();
            var mainWindow = new MainWindow(
                [fixtureDirectory],
                Path.Combine(dataDirectory, "settings.json"))
            {
                ShowInTaskbar = false
            };
            var exitCode = 1;
            var scenarioStarted = false;
            mainWindow.ContentRendered += (_, _) =>
            {
                if (scenarioStarted)
                {
                    return;
                }

                scenarioStarted = true;
                mainWindow.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        exitCode = RunScenario(
                            mainWindow,
                            outputDirectory,
                            dataDirectory,
                            firstPath,
                            beforeHash,
                            beforeTimestamp);
                        mainWindow.Close();
                        application.Shutdown();
                    }));
            };
            application.Run(mainWindow);
            return exitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SCREENSHOT_HUB_UI_SMOKE_FAILURE");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunScenario(
        MainWindow mainWindow,
        string outputDirectory,
        string dataDirectory,
        string firstPath,
        string beforeHash,
        DateTime beforeTimestamp)
    {
        try
        {
            PumpUntil(
                () => !mainWindow.ViewModel.IsBusy && mainWindow.ViewModel.VisibleItems.Count == 3,
                TimeSpan.FromSeconds(30),
                "The main gallery did not finish loading three fixture images.");
            PumpUntil(
                () => mainWindow.ViewModel.VisibleItems.All(item => !item.IsThumbnailLoading),
                TimeSpan.FromSeconds(15),
                "Gallery thumbnails did not finish loading.");

            if (!mainWindow.ViewModel.GetUserDataSnapshot(firstPath).IsFavorite)
            {
                WaitTask(mainWindow.ViewModel.ToggleFavoriteAsync(firstPath));
            }
            WaitTask(mainWindow.ViewModel.UpdateTagsAsync(firstPath, ["夜景", "お気に入り"]));
            mainWindow.ViewModel.AnalyzeCommand.Execute(null);
            var analysisPath = Path.Combine(dataDirectory, "analysis-v1.json");
            PumpUntil(
                () => File.Exists(analysisPath) && !mainWindow.ViewModel.IsAnalyzing,
                TimeSpan.FromSeconds(30),
                "Duplicate analysis did not complete.");
            PumpUntil(
                () => mainWindow.ViewModel.VisibleItems.All(item => !item.IsThumbnailLoading),
                TimeSpan.FromSeconds(15),
                "Thumbnails did not settle after analysis.");

            mainWindow.ViewModel.SelectedGalleryFilter = mainWindow.ViewModel.GalleryFilters.Single(option =>
                option.Kind == ScreenshotHub.ViewModels.GalleryFilterKind.Favorites);
            if (mainWindow.ViewModel.VisibleItems.Count != 1 ||
                !mainWindow.ViewModel.VisibleItems[0].FilePath.Equals(firstPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The favorites filter did not isolate the favorite image.");
            }

            mainWindow.ViewModel.SelectedGalleryFilter = mainWindow.ViewModel.GalleryFilters.Single(option =>
                option.Kind == ScreenshotHub.ViewModels.GalleryFilterKind.ExactDuplicates);
            if (mainWindow.ViewModel.VisibleItems.Count != 2)
            {
                throw new InvalidOperationException("The exact-duplicate filter did not show both copies.");
            }

            mainWindow.ViewModel.SelectedGalleryFilter = mainWindow.ViewModel.GalleryFilters[0];
            mainWindow.ViewModel.SelectedTagFilter = mainWindow.ViewModel.TagFilters.Single(option =>
                string.Equals(option.Tag, "夜景", StringComparison.Ordinal));
            if (mainWindow.ViewModel.VisibleItems.Count != 1)
            {
                throw new InvalidOperationException("The tag filter did not isolate the tagged image.");
            }

            mainWindow.ViewModel.SelectedTagFilter = mainWindow.ViewModel.TagFilters[0];
            PumpUntil(
                () => mainWindow.ViewModel.VisibleItems.Count == 3 &&
                      mainWindow.ViewModel.VisibleItems.All(item => !item.IsThumbnailLoading),
                TimeSpan.FromSeconds(15),
                "The gallery did not return to the unfiltered view.");
            if (!mainWindow.ViewModel.CanAnalyze)
            {
                throw new InvalidOperationException("Duplicate analysis did not become available again after completion.");
            }
            RunBrowseScenario(mainWindow, outputDirectory, dataDirectory, firstPath);
            Capture(mainWindow, Path.Combine(outputDirectory, "main-window.png"));

            var tagEditor = new TagEditorWindow(["夜景", "お気に入り"])
            {
                Owner = mainWindow,
                ShowInTaskbar = false
            };
            tagEditor.Show();
            PumpFor(TimeSpan.FromMilliseconds(250));
            Capture(tagEditor, Path.Combine(outputDirectory, "tag-editor.png"));
            tagEditor.Close();

            var viewer = new ImageViewerWindow(
                mainWindow.ViewModel,
                mainWindow.ViewModel.GetViewerRecords(),
                firstPath)
            {
                Owner = mainWindow,
                ShowInTaskbar = false
            };
            viewer.Show();
            PumpFor(TimeSpan.FromMilliseconds(500));
            Capture(viewer, Path.Combine(outputDirectory, "image-viewer.png"));
            viewer.Close();

            var afterHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstPath)));
            var afterTimestamp = File.GetLastWriteTimeUtc(firstPath);
            if (afterHash != beforeHash || afterTimestamp != beforeTimestamp)
            {
                throw new InvalidOperationException("UI operations changed the fixture image.");
            }

            var report = new
            {
                language = AppText.LanguageCode,
                mainTitle = AppText.WindowTitle,
                screenshots = 3,
                favoritePersisted = File.Exists(Path.Combine(dataDirectory, "user-data-v1.json")),
                analysisPersisted = File.Exists(analysisPath),
                imageHashUnchanged = afterHash == beforeHash,
                imageTimestampUnchanged = afterTimestamp == beforeTimestamp,
                dateAndSortControls = true,
                browseSettingsPersisted = true,
                captures = new[] { "main-window.png", "date-filter.png", "date-filter-minimum.png", "date-calendar.png", "tag-editor.png", "image-viewer.png" }
            };
            File.WriteAllText(
                Path.Combine(outputDirectory, "report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("SCREENSHOT_HUB_UI_SMOKE_SUCCESS");
            Console.WriteLine(JsonSerializer.Serialize(report));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SCREENSHOT_HUB_UI_SMOKE_FAILURE");
            Console.Error.WriteLine(exception);
            try
            {
                Capture(mainWindow, Path.Combine(outputDirectory, "failure-main-window.png"));
                Console.Error.WriteLine(string.Join(
                    Environment.NewLine,
                    mainWindow.ViewModel.VisibleItems.Select(item =>
                        $"{item.FileName}: loading={item.IsThumbnailLoading}, unavailable={item.IsThumbnailUnavailable}, source={item.Thumbnail is not null}")));
            }
            catch (Exception captureException)
            {
                Console.Error.WriteLine($"Failure capture also failed: {captureException.Message}");
            }
            return 1;
        }
    }

    private static void RunBrowseScenario(MainWindow window, string outputDirectory, string dataDirectory, string firstPath)
    {
        var vm = window.ViewModel;
        var periodBox = (ComboBox)window.FindName("DatePeriodBox");
        var sortBox = (ComboBox)window.FindName("SortOrderBox");
        var fromPicker = (DatePicker)window.FindName("DateFromPicker");
        var toPicker = (DatePicker)window.FindName("DateToPicker");
        var date = File.GetLastWriteTime(firstPath).Date;
        var records = vm.GetViewerRecords();
        var beforeImages = records.ToDictionary(record => record.FilePath, record =>
            (Hash: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(record.FilePath))),
             Time: File.GetLastWriteTimeUtc(record.FilePath)));
        var catalogWrites = Directory.GetFiles(dataDirectory, "*v1-*.json")
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);

        foreach (var period in new[] { "today", "last-7-days", "last-30-days" })
        {
            periodBox.SelectedItem = vm.DatePeriods.Single(option => option.Key == period);
            var expected = period == "last-30-days" ? 3 : 2;
            if (vm.VisibleItems.Count != expected)
                throw new InvalidOperationException($"The {period} UI date filter returned the wrong images.");
        }

        periodBox.SelectedItem = vm.DatePeriods.Single(option => option.Key == "custom");
        fromPicker.SelectedDate = date;
        toPicker.SelectedDate = date;
        if (vm.VisibleItems.Count != 2 || !vm.IsCustomDatePeriod)
            throw new InvalidOperationException("Bound date pickers did not apply an inclusive one-day range.");

        vm.SelectedGalleryFilter = vm.GalleryFilters.Single(option => option.Kind == GalleryFilterKind.Favorites);
        if (vm.VisibleItems.Count != 1 || vm.VisibleItems[0].FilePath != firstPath)
            throw new InvalidOperationException("The date filter did not combine with favorites.");
        vm.SelectedGalleryFilter = vm.GalleryFilters[0];

        fromPicker.SelectedDate = date.AddDays(1);
        if (!vm.HasInvalidDateRange || vm.VisibleItems.Count != 0)
            throw new InvalidOperationException("An inverted date range was not clearly rejected.");
        fromPicker.SelectedDate = null;
        if (vm.VisibleItems.Count != 3 || vm.HasInvalidDateRange)
            throw new InvalidOperationException("Clearing the start date did not remove its lower bound.");
        fromPicker.SelectedDate = date;
        sortBox.SelectedItem = vm.SortOrders.Single(option => option.Key == "oldest");

        // Wait for the asynchronous atomic settings save, then verify a separate store sees it.
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        PumpUntil(() =>
        {
            try
            {
                using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                return root.GetProperty("datePeriod").GetString() == "custom" &&
                       root.GetProperty("sortOrder").GetString() == "oldest" &&
                       root.GetProperty("dateFrom").GetDateTime().Date == date &&
                       root.GetProperty("dateTo").GetDateTime().Date == date;
            }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(10), "Browse preferences were not saved.");
        var reload = new SettingsStore(settingsPath).LoadAsync();
        WaitTask(reload);
        if (reload.Result.DatePeriod != "custom" || reload.Result.SortOrder != "oldest" ||
            reload.Result.DateFrom != date || reload.Result.DateTo != date)
            throw new InvalidOperationException("Browse preferences were lost when reopened.");

        PumpUntil(() => vm.VisibleItems.All(item => !item.IsThumbnailLoading),
            TimeSpan.FromSeconds(15), "Date-filter thumbnails did not settle.");
        Capture(window, Path.Combine(outputDirectory, "date-filter.png"));
        fromPicker.IsDropDownOpen = true;
        PumpFor(TimeSpan.FromMilliseconds(150));
        var calendarPopup = (Popup)fromPicker.Template.FindName("PART_Popup", fromPicker);
        if (!calendarPopup.IsOpen || calendarPopup.Child is not FrameworkElement calendar)
            throw new InvalidOperationException("The calendar picker did not open.");
        CaptureElement(calendar, Path.Combine(outputDirectory, "date-calendar.png"));
        fromPicker.IsDropDownOpen = false;
        var width = window.Width;
        var height = window.Height;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        PumpFor(TimeSpan.FromMilliseconds(150));
        Capture(window, Path.Combine(outputDirectory, "date-filter-minimum.png"));
        window.Width = width;
        window.Height = height;

        vm.ClearDateFilterCommand.Execute(null);
        if (vm.HasDateFilter || vm.DateFrom is not null || vm.DateTo is not null || vm.VisibleItems.Count != 3)
            throw new InvalidOperationException("Clear dates did not restore the whole gallery.");
        if (vm.VisibleItems[0].FileName != "戦闘-002.png")
            throw new InvalidOperationException("Oldest-first sorting was not applied in the gallery.");
        sortBox.SelectedItem = vm.SortOrders.Single(option => option.Key == "largest");
        if (vm.VisibleItems[0].FileName != "戦闘-002.png" ||
            vm.GetViewerRecords()[0].FilePath != vm.VisibleItems[0].FilePath)
            throw new InvalidOperationException("The gallery and viewer did not share size ordering.");
        sortBox.SelectedItem = vm.SortOrders[0];
        if (vm.VisibleItems[0].FileName == "戦闘-002.png")
            throw new InvalidOperationException("Newest-first sorting did not restore the recent screenshots.");
        vm.FolderOnlyMode = true;
        if (vm.VisibleItems.Count != 0 || periodBox.IsVisible)
            throw new InvalidOperationException("Lightweight mode still displayed gallery date controls.");
        vm.FolderOnlyMode = false;
        PumpUntil(() => vm.VisibleItems.Count == 3 && vm.VisibleItems.All(item => !item.IsThumbnailLoading),
            TimeSpan.FromSeconds(15), "The gallery did not return after lightweight mode.");
        if (vm.IsBusy || catalogWrites.Any(pair => File.GetLastWriteTimeUtc(pair.Key) != pair.Value))
            throw new InvalidOperationException("Browsing dates or sorting triggered a scan or rewrote a catalog.");
        if (beforeImages.Any(pair =>
                File.GetLastWriteTimeUtc(pair.Key) != pair.Value.Time ||
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) != pair.Value.Hash))
            throw new InvalidOperationException("Browsing dates or sorting changed a source image.");
    }

    private static byte[] CreatePng(int width, int height, int variant)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                if (variant == 0)
                {
                    pixels[offset] = (byte)(70 + x * 80 / width);
                    pixels[offset + 1] = (byte)(30 + y * 70 / height);
                    pixels[offset + 2] = (byte)(110 + x * 100 / width);
                }
                else
                {
                    pixels[offset] = (byte)(25 + y * 180 / height);
                    pixels[offset + 1] = (byte)(120 + x * 100 / width);
                    pixels[offset + 2] = (byte)(40 + y * 80 / height);
                }

                pixels[offset + 3] = 255;
            }
        }

        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Capture(Window window, string outputPath)
        => CaptureElement(window, outputPath);

    private static void CaptureElement(FrameworkElement window, string outputPath)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void WaitTask(Task task)
    {
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(15), "An asynchronous UI operation timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void PumpFor(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        PumpUntil(() => DateTime.UtcNow >= deadline, duration + TimeSpan.FromSeconds(2), "Dispatcher delay timed out.");
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        if (condition())
        {
            return;
        }

        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow + timeout;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        timer.Tick += (_, _) =>
        {
            if (condition())
            {
                timer.Stop();
                frame.Continue = false;
            }
            else if (DateTime.UtcNow >= deadline)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!condition())
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
