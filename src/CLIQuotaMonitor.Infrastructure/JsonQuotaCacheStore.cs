using System.Text.Json;
using System.Text.Json.Serialization;
using CLIQuotaMonitor.Core.Caching;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class JsonQuotaCacheStore : IQuotaCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonQuotaCacheStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<QuotaSnapshot?> GetAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshots = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return snapshots.TryGetValue(providerId, out var snapshot) ? snapshot : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(
        QuotaSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshots = await LoadAsync(cancellationToken).ConfigureAwait(false);
            snapshots[snapshot.ProviderId] = snapshot;
            await SaveAsync(snapshots, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, QuotaSnapshot>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, QuotaSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, QuotaSnapshot>>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? new Dictionary<string, QuotaSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, QuotaSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, QuotaSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveAsync(
        Dictionary<string, QuotaSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshots, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
