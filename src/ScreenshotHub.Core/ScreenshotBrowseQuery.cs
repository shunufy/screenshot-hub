namespace ScreenshotHub.Core;

/// <summary>Queries cached file metadata without opening images or visiting folders.</summary>
public static class ScreenshotBrowseQuery
{
    public static string NormalizePeriod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "today" => "today",
        "last-7-days" => "last-7-days",
        "last-30-days" => "last-30-days",
        "custom" => "custom",
        _ => "all-time"
    };

    public static string NormalizeSort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "oldest" => "oldest",
        "name" => "name",
        "largest" => "largest",
        _ => "newest"
    };

    public static IEnumerable<ScreenshotRecord> FilterByDate(
        IEnumerable<ScreenshotRecord> records,
        string? period,
        DateTime? from,
        DateTime? to,
        DateTime localToday,
        TimeZoneInfo? timeZone = null)
    {
        var today = localToday.Date;
        var (start, end) = NormalizePeriod(period) switch
        {
            "today" => ((DateTime?)today, (DateTime?)today),
            "last-7-days" => (today.AddDays(-6), (DateTime?)today),
            "last-30-days" => (today.AddDays(-29), (DateTime?)today),
            "custom" => (from?.Date, to?.Date),
            _ => ((DateTime?)null, (DateTime?)null)
        };
        if (start is null && end is null)
        {
            return records;
        }

        if (start > end)
        {
            return [];
        }

        var zone = timeZone ?? TimeZoneInfo.Local;
        return records.Where(record =>
        {
            var utc = DateTime.SpecifyKind(record.LastWriteTimeUtc, DateTimeKind.Utc);
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
            return (start is null || localDate >= start.Value) &&
                   (end is null || localDate <= end.Value);
        });
    }

    public static IOrderedEnumerable<ScreenshotRecord> Sort(
        IEnumerable<ScreenshotRecord> records,
        string? sort,
        Func<ScreenshotRecord, string?>? groupKey = null)
    {
        // Duplicate groups stay together; the requested order applies inside each group.
        var grouped = groupKey is null ? null : records.OrderBy(groupKey, StringComparer.Ordinal);
        var ordered = NormalizeSort(sort) switch
        {
            "oldest" => grouped?.ThenBy(record => record.LastWriteTimeUtc) ??
                        records.OrderBy(record => record.LastWriteTimeUtc),
            "name" => grouped?.ThenBy(record => Path.GetFileName(record.FilePath), StringComparer.CurrentCultureIgnoreCase) ??
                      records.OrderBy(record => Path.GetFileName(record.FilePath), StringComparer.CurrentCultureIgnoreCase),
            "largest" => grouped?.ThenByDescending(record => record.FileSize) ??
                         records.OrderByDescending(record => record.FileSize),
            _ => grouped?.ThenByDescending(record => record.LastWriteTimeUtc) ??
                 records.OrderByDescending(record => record.LastWriteTimeUtc)
        };
        return ordered.ThenBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase);
    }
}
