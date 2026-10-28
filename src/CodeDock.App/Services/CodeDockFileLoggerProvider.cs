using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CodeDock.App.Services;

public sealed class CodeDockFileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _writer;

    public CodeDockFileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = Task.Run(WriteLinesAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, Enqueue);

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        if (!_writer.Wait(TimeSpan.FromSeconds(2)))
        {
            _cancellation.Cancel();
        }

        _cancellation.Dispose();
    }

    private void Enqueue(string line) => _lines.Writer.TryWrite(line);

    private async Task WriteLinesAsync()
    {
        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream);
            await foreach (var line in _lines.Reader.ReadAllAsync(_cancellation.Token))
            {
                await writer.WriteLineAsync(line);
                await writer.FlushAsync(_cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("CodeDock file logging stopped before the queue was fully drained.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"CodeDock file logging failed: {exception.Message}");
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly Action<string> _enqueue;

        public FileLogger(string category, Action<string> enqueue)
        {
            _category = category;
            _enqueue = enqueue;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:O} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            _enqueue(line);
        }
    }
}
