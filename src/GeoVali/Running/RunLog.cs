using System.Globalization;

namespace GeoVali.Running;

public sealed record LogEntry(DateTime timestampUtc, string level, string message);

/// <summary>
/// A rolling log file in the config directory holds detail; the dashboard shows the recent
/// in-memory buffer. Neither ever contains the cookie: every message is redacted on the way in,
/// so the rule holds no matter who writes the message.
/// </summary>
public sealed class RunLog(string logDirectory, Func<string?> cookieProvider)
{
    public const int BufferSize = 500;
    public const int RetentionDays = 14;

    private readonly Queue<LogEntry> _buffer = new(BufferSize);
    private readonly Lock _gate = new();

    /// <summary>Raised for every entry, so the SSE endpoint can push it to the dashboard.</summary>
    public event Action<LogEntry>? Appended;

    public void Info(string message) => Append("info", message);

    public void Error(string message) => Append("error", message);

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    public void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(logDirectory, "geovali-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Pruning is housekeeping. Never let it matter.
        }
    }

    private void Append(string level, string message)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, Redact(message));

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > BufferSize)
            {
                _buffer.Dequeue();
            }
        }

        WriteToFile(entry);
        Appended?.Invoke(entry);
    }

    private string Redact(string message)
    {
        var cookie = cookieProvider();
        return string.IsNullOrEmpty(cookie) || cookie.Length < 8
            ? message
            : message.Replace(cookie, "***", StringComparison.Ordinal);
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, $"geovali-{entry.timestampUtc:yyyy-MM-dd}.log");
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{entry.timestampUtc:yyyy-MM-ddTHH:mm:ssZ} [{entry.level}] {entry.message}{Environment.NewLine}");
            File.AppendAllText(path, line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Logging is a side channel; a failure to write must never take down a run.
        }
    }
}
