using System.IO;
using System.Text.Json;

namespace ScreenshotHub.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;
    private readonly string _backupPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotHub",
            "settings.json"));
        _backupPath = _settingsPath + ".backup";
    }

    public async Task<HubSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryLoadAsync(_settingsPath, cancellationToken).ConfigureAwait(false) ??
                   await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false) ??
                   Normalize(new HubSettings());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        HubSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath) ??
                            throw new InvalidOperationException(AppText.InvalidSettingsPath);
            Directory.CreateDirectory(directory);

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
            var normalized = Normalize(settings);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(_settingsPath))
            {
                File.Move(temporaryPath, _settingsPath);
                temporaryPath = null;
                return;
            }

            // Only a known-good current file may replace the last known-good backup.
            var currentIsValid = await TryLoadAsync(_settingsPath, cancellationToken).ConfigureAwait(false) is not null;
            File.Replace(
                temporaryPath,
                _settingsPath,
                currentIsValid ? _backupPath : null,
                ignoreMetadataErrors: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A later save uses a unique temporary name.
                }
            }

            _gate.Release();
        }
    }

    private static async Task<HubSettings?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<HubSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return settings is null ? null : Normalize(settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static HubSettings Normalize(HubSettings settings)
    {
        if (settings.SchemaVersion < HubSettings.CurrentSchemaVersion)
        {
            // v0.2.0 enabled recurring scans by default. Disable that legacy default once
            // so slower disks do not keep performing full scans in the background.
            settings.AutoRefresh = false;
            settings.AutoRefreshMinutes = 30;
        }

        settings.SchemaVersion = HubSettings.CurrentSchemaVersion;
        settings.AutoRefreshMinutes = Math.Clamp(settings.AutoRefreshMinutes, 1, 120);
        settings.MaxDepth = Math.Clamp(settings.MaxDepth, 1, 64);
        settings.CustomRoots = (settings.CustomRoots ?? [])
            .Select(PathUtility.Normalize)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.IgnoredRoots = (settings.IgnoredRoots ?? [])
            .Select(PathUtility.Normalize)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return settings;
    }
}
