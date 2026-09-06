using GeoVali.Autostart;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class AutostartTests
{
    [Fact]
    public void Mac_writes_and_removes_a_launch_agent_plist()
    {
        using var temp = new TempDir();
        var autostart = new MacAutostart("/usr/local/bin/geovali", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var plist = Path.Combine(temp.Path, "com.geovali.agent.plist");
        Assert.True(File.Exists(plist));
        var text = File.ReadAllText(plist);
        Assert.Contains("com.geovali.agent", text);
        Assert.Contains("/usr/local/bin/geovali", text);
        Assert.Contains("<key>RunAtLoad</key>", text);
        Assert.True(autostart.IsEnabled());

        autostart.Disable();

        Assert.False(File.Exists(plist));
        Assert.False(autostart.IsEnabled());
    }

    [Fact]
    public void Linux_writes_and_removes_a_systemd_user_unit()
    {
        using var temp = new TempDir();
        var autostart = new LinuxAutostart("/home/perhel/.dotnet/tools/geovali", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var unit = Path.Combine(temp.Path, "geovali.service");
        Assert.True(File.Exists(unit));
        var text = File.ReadAllText(unit);
        Assert.Contains("ExecStart=/home/perhel/.dotnet/tools/geovali", text);
        Assert.Contains("WantedBy=default.target", text);
        Assert.True(autostart.IsEnabled());

        autostart.Disable();

        Assert.False(File.Exists(unit));
    }

    [Fact]
    public void Windows_writes_and_removes_a_startup_folder_shortcut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // The .url file it writes is only meaningful on Windows.
        }

        using var temp = new TempDir();
        var autostart = new WindowsStartupShortcut(@"C:\Users\perhel\.dotnet\tools\geovali.exe", temp.Path);

        Assert.False(autostart.IsEnabled());

        autostart.Enable();

        var shortcut = Path.Combine(temp.Path, "GeoVali.url");
        Assert.True(File.Exists(shortcut));
        Assert.Contains("geovali.exe", File.ReadAllText(shortcut));

        autostart.Disable();

        Assert.False(File.Exists(shortcut));
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        using var temp = new TempDir();
        var autostart = new LinuxAutostart("/bin/geovali", temp.Path);

        autostart.Enable();
        autostart.Enable();

        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Disable_on_something_never_enabled_is_a_no_op()
    {
        using var temp = new TempDir();
        new LinuxAutostart("/bin/geovali", temp.Path).Disable();
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Every_implementation_describes_what_it_does_in_plain_words()
    {
        using var temp = new TempDir();

        Assert.Contains("Startup folder", new WindowsStartupShortcut("v", temp.Path).Describe());
        Assert.Contains("LaunchAgent", new MacAutostart("v", temp.Path).Describe());
        Assert.Contains("systemd", new LinuxAutostart("v", temp.Path).Describe());
        Assert.Contains("not supported", new UnsupportedAutostart().Describe());
    }

    [Fact]
    public void The_unsupported_implementation_never_throws()
    {
        var autostart = new UnsupportedAutostart();

        autostart.Enable();
        autostart.Disable();

        Assert.False(autostart.IsEnabled());
    }

    [Fact]
    public void The_factory_returns_something_usable_on_this_machine()
    {
        using var temp = new TempDir();
        Assert.NotNull(AutostartFactory.Create("geovali", temp.Path));
    }

    /// <summary>Stands in for schtasks.exe, recording every command line GeoVali builds.</summary>
    private sealed class FakeSchtasks
    {
        public List<string[]> Calls { get; } = [];

        /// <summary>Verb ("/create", "/query", "/delete") to the exit code it should return.</summary>
        public Dictionary<string, int> ExitCodeByVerb { get; } = new(StringComparer.Ordinal);

        public int Run(IReadOnlyList<string> arguments)
        {
            Calls.Add([.. arguments]);
            return ExitCodeByVerb.GetValueOrDefault(arguments[0], 0);
        }
    }

    [Fact]
    public void Windows_task_is_registered_to_run_at_logon_with_no_window_and_no_password()
    {
        var schtasks = new FakeSchtasks();

        new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).Enable();

        Assert.Equal(
            ["/create", "/tn", "GeoVali", "/tr", "\"C:\\Tools\\geovali.exe\" --no-browser",
             "/sc", "onlogon", "/np", "/f"],
            Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_reports_enabled_when_the_query_finds_it()
    {
        var schtasks = new FakeSchtasks();

        var enabled = new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).IsEnabled();

        Assert.True(enabled);
        Assert.Equal(["/query", "/tn", "GeoVali"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_reports_disabled_when_the_query_does_not_find_it()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/query"] = 1 } };

        Assert.False(new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).IsEnabled());
    }

    [Fact]
    public void Windows_task_is_deleted_without_a_prompt()
    {
        var schtasks = new FakeSchtasks();

        new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).Disable();

        Assert.Equal(["/delete", "/tn", "GeoVali", "/f"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_delete_tolerates_a_task_that_was_never_there()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/delete"] = 1 } };

        new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).Disable();
    }

    [Fact]
    public void Windows_task_enable_says_so_when_schtasks_refuses()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/create"] = 1 } };

        Assert.Throws<AutostartException>(
            () => new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run).Enable());
    }

    [Fact]
    public void Windows_prefers_the_scheduled_task_over_a_startup_shortcut()
    {
        using var temp = new TempDir();
        var schtasks = new FakeSchtasks();
        var autostart = WindowsAutostartFor(temp, schtasks);

        autostart.Enable();

        Assert.Equal("/create", Assert.Single(schtasks.Calls)[0]);
        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.Contains("Scheduled Task", autostart.Describe());
    }

    [Fact]
    public void Windows_falls_back_to_a_startup_shortcut_when_the_task_cannot_be_registered()
    {
        using var temp = new TempDir();
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/create"] = 1, ["/query"] = 1 } };
        var autostart = WindowsAutostartFor(temp, schtasks);

        autostart.Enable();

        Assert.True(File.Exists(Path.Combine(temp.Path, "GeoVali.url")));
        Assert.True(autostart.IsEnabled());
        Assert.Contains("Startup folder", autostart.Describe());
    }

    [Fact]
    public void Windows_disable_clears_both_mechanisms()
    {
        using var temp = new TempDir();
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/create"] = 1, ["/query"] = 1 } };
        var autostart = WindowsAutostartFor(temp, schtasks);
        autostart.Enable();

        autostart.Disable();

        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.Contains(schtasks.Calls, call => call[0] == "/delete");
        Assert.False(autostart.IsEnabled());
    }

    private static WindowsAutostart WindowsAutostartFor(TempDir temp, FakeSchtasks schtasks) =>
        new(new WindowsTaskAutostart(@"C:\Tools\geovali.exe", schtasks.Run),
            new WindowsStartupShortcut(@"C:\Tools\geovali.exe", temp.Path));
}
