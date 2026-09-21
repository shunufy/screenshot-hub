using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotHub.Core;

namespace ScreenshotHub.Services;

internal sealed record ImageAnalysisProgress(int Completed, int Total, string CurrentPath);

internal sealed class ImageAnalysisService
{
    public async Task<IReadOnlyList<ScreenshotAnalysisRecord>> AnalyzeAsync(
        IReadOnlyCollection<ScreenshotRecord> screenshots,
        IReadOnlyCollection<ScreenshotAnalysisRecord> cachedAnalyses,
        IProgress<ImageAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screenshots);
        ArgumentNullException.ThrowIfNull(cachedAnalyses);

        var ordered = screenshots
            .OrderBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var cachedByPath = cachedAnalyses
            .GroupBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var duplicateSizedFiles = ordered
            .GroupBy(record => record.FileSize)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(record => record.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ScreenshotAnalysisRecord>(ordered.Length);

        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screenshot = ordered[index];
            var needsSha256 = duplicateSizedFiles.Contains(screenshot.FilePath);
            cachedByPath.TryGetValue(screenshot.FilePath, out var cached);
            if (cached is not null && IsCurrent(cached, screenshot) &&
                cached.DifferenceHash is not null &&
                (!needsSha256 || cached.Sha256 is not null))
            {
                results.Add(cached);
            }
            else
            {
                try
                {
                    var visual = cached is not null && IsCurrent(cached, screenshot) &&
                                 cached.DifferenceHash is not null
                        ? (cached.DifferenceHash, cached.PixelWidth, cached.PixelHeight)
                        : await Task.Run(
                            () => ReadVisualSignature(screenshot.FilePath),
                            cancellationToken).ConfigureAwait(false);
                    var sha256 = needsSha256
                        ? cached is not null && IsCurrent(cached, screenshot) && cached.Sha256 is not null
                            ? cached.Sha256
                            : await ComputeSha256Async(screenshot.FilePath, cancellationToken).ConfigureAwait(false)
                        : cached is not null && IsCurrent(cached, screenshot)
                            ? cached.Sha256
                            : null;
                    results.Add(new ScreenshotAnalysisRecord(
                        screenshot.FilePath,
                        screenshot.LastWriteTimeUtc,
                        screenshot.FileSize,
                        sha256,
                        visual.DifferenceHash,
                        visual.PixelWidth,
                        visual.PixelHeight));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or NotSupportedException or
                        FileFormatException or System.Security.SecurityException)
                {
                    results.Add(new ScreenshotAnalysisRecord(
                        screenshot.FilePath,
                        screenshot.LastWriteTimeUtc,
                        screenshot.FileSize,
                        null,
                        null,
                        0,
                        0));
                }
            }

            progress?.Report(new ImageAnalysisProgress(index + 1, ordered.Length, screenshot.FilePath));
        }

        return results;
    }

    private static bool IsCurrent(ScreenshotAnalysisRecord analysis, ScreenshotRecord screenshot)
        => analysis.FileSize == screenshot.FileSize &&
           analysis.LastWriteTimeUtc == screenshot.LastWriteTimeUtc;

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static (ulong? DifferenceHash, int PixelWidth, int PixelHeight) ReadVisualSignature(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault();
        if (frame is null || frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            return (null, 0, 0);
        }

        var sourceWidth = frame.PixelWidth;
        var sourceHeight = frame.PixelHeight;
        var scaled = new TransformedBitmap(
            frame,
            new ScaleTransform(9d / sourceWidth, 8d / sourceHeight));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        if (converted.PixelWidth < 9 || converted.PixelHeight < 8)
        {
            return (null, sourceWidth, sourceHeight);
        }

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        ulong differenceHash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++, bit++)
            {
                var left = Luminance(pixels, y * stride + x * 4);
                var right = Luminance(pixels, y * stride + (x + 1) * 4);
                if (left > right)
                {
                    differenceHash |= 1UL << bit;
                }
            }
        }

        return (differenceHash, sourceWidth, sourceHeight);
    }

    private static int Luminance(byte[] pixels, int offset)
    {
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return (red * 299 + green * 587 + blue * 114) / 1000;
    }
}
