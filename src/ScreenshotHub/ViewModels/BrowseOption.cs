using ScreenshotHub.Core;

namespace ScreenshotHub.ViewModels;

internal sealed record BrowseOption(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;

    public static IReadOnlyList<BrowseOption> DatePeriods() =>
    [
        new("all-time", AppText.DateAllTime),
        new("today", AppText.DateToday),
        new("last-7-days", AppText.DateLast7Days),
        new("last-30-days", AppText.DateLast30Days),
        new("custom", AppText.DateCustom)
    ];

    public static IReadOnlyList<BrowseOption> SortOrders() =>
    [
        new("newest", AppText.SortNewest),
        new("oldest", AppText.SortOldest),
        new("name", AppText.SortName),
        new("largest", AppText.SortLargest)
    ];
}
