using System.Text.Json;
using System.Text.Json.Serialization;
using CLIQuotaMonitor.Core.Models;

namespace CLIQuotaMonitor.Infrastructure;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public JsonSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stream = File.Create(temporaryPath);
            try
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
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
