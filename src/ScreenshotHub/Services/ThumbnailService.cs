using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenshotHub.Services;

internal sealed class ThumbnailService
{
    private const int CacheCapacity = 300;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly SemaphoreSlim _decodeSlots = new(6, 6);

    public async Task<ImageSource?> GetAsync(
        string filePath,
        DateTime lastWriteTimeUtc,
        long fileSize,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{filePath}|{lastWriteTimeUtc.Ticks}|{fileSize}";
        if (TryGetCached(cacheKey, out var cached))
        {
            return cached;
        }

        await _decodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(cacheKey, out cached))
            {
                return cached;
            }

            var thumbnail = await Task.Run(
                () => Decode(filePath),
                cancellationToken).ConfigureAwait(false);

            if (thumbnail is not null)
            {
                AddToCache(cacheKey, thumbnail);
            }

            return thumbnail;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _decodeSlots.Release();
        }
    }

    private bool TryGetCached(string key, out ImageSource? source)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out var entry))
            {
                source = null;
                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            source = entry.Source;
            return true;
        }
    }

    private void AddToCache(string key, ImageSource source)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.Node);
            }

            var node = _lru.AddFirst(key);
            _cache[key] = new CacheEntry(source, node);

            while (_cache.Count > CacheCapacity && _lru.Last is { } oldest)
            {
                _cache.Remove(oldest.Value);
                _lru.RemoveLast();
            }
        }
    }

    private static ImageSource? Decode(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = 360;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record CacheEntry(ImageSource Source, LinkedListNode<string> Node);
}
