using GeoVali.Running;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class RunLogTests
{
    [Fact]
    public void Keeps_recent_entries_in_memory_for_the_dashboard()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        log.Info("Regenerating An Arbitrary Africa");
        log.Error("Publishing failed");

        var recent = log.Recent();
        Assert.Equal(2, recent.Count);
        Assert.Equal("info", recent[0].level);
        Assert.Equal("Regenerating An Arbitrary Africa", recent[0].message);
        Assert.Equal("error", recent[1].level);
    }

    [Fact]
    public void Caps_the_in_memory_buffer_and_keeps_the_newest()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        for (var i = 0; i < RunLog.BufferSize + 50; i++)
        {
            log.Info($"line {i}");
        }

        var recent = log.Recent();
        Assert.Equal(RunLog.BufferSize, recent.Count);
        Assert.Equal($"line {RunLog.BufferSize + 49}", recent[^1].message);
        Assert.Equal("line 50", recent[0].message);
    }

    [Fact]
    public void Writes_to_a_dated_file_in_the_log_directory()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);

        log.Info("Regenerating An Arbitrary Africa");

        var file = Directory.GetFiles(temp.Path, "geovali-*.log").Single();
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd"), file);
        Assert.Contains("Regenerating An Arbitrary Africa", File.ReadAllText(file));
    }

    [Fact]
    public void Never_lets_the_cookie_reach_the_file_the_buffer_or_a_subscriber()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => "ncfa-super-secret-value");
        var seen = new List<LogEntry>();
        log.Appended += seen.Add;

        // Something careless interpolated the cookie into a message.
        log.Error("Request failed with Cookie: _ncfa=ncfa-super-secret-value");

        var fileText = File.ReadAllText(Directory.GetFiles(temp.Path, "geovali-*.log").Single());
        Assert.DoesNotContain("ncfa-super-secret-value", fileText);
        Assert.DoesNotContain("ncfa-super-secret-value", log.Recent().Single().message);
        Assert.DoesNotContain("ncfa-super-secret-value", seen.Single().message);
        Assert.Contains("***", log.Recent().Single().message);
    }

    [Fact]
    public void Raises_appended_for_live_dashboard_streaming()
    {
        using var temp = new TempDir();
        var log = new RunLog(temp.Path, () => null);
        var seen = new List<LogEntry>();
        log.Appended += seen.Add;

        log.Info("Finding locations in Botswana");

        Assert.Equal("Finding locations in Botswana", seen.Single().message);
    }

    [Fact]
    public void Prunes_log_files_older_than_the_retention_window()
    {
        using var temp = new TempDir();
        var old = Path.Combine(temp.Path, "geovali-2020-01-01.log");
        var recent = Path.Combine(temp.Path, $"geovali-{DateTime.UtcNow:yyyy-MM-dd}.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-(RunLog.RetentionDays + 1)));

        new RunLog(temp.Path, () => null).PruneOldFiles();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Survives_a_log_directory_it_cannot_write_to()
    {
        // Logging is a side channel; a failure to write must never take down a run.
        var unwritable = Path.Combine(Path.GetTempPath(), "geovali-tests", Guid.NewGuid().ToString("N"), "\0bad");
        var log = new RunLog(unwritable, () => null);

        log.Info("still buffered");

        Assert.Equal("still buffered", log.Recent().Single().message);
    }
}
