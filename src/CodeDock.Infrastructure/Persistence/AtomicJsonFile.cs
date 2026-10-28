using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CodeDock.Infrastructure.Persistence;

internal sealed class AtomicJsonFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger _logger;

    public AtomicJsonFile(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The persistence path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backupPath = $"{path}.bak";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                ReplaceWithBackup(temporaryPath, path, backupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string QuarantineCorruptFile(string path)
    {
        var quarantinedPath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        File.Move(path, quarantinedPath);
        _logger.LogWarning("Quarantined corrupt CodeDock data file {SourcePath} as {QuarantinedPath}", path, quarantinedPath);
        return quarantinedPath;
    }

    private static void ReplaceWithBackup(string temporaryPath, string destinationPath, string backupPath)
    {
        try
        {
            File.Replace(temporaryPath, destinationPath, backupPath, true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(destinationPath, backupPath, true);
            File.Move(temporaryPath, destinationPath, true);
        }
    }
}
