using System.ComponentModel;
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

        Assert.Contains("Scheduled Task", TaskAutostart(new FakeSchtasks(), new FakeElevation()).Describe());
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

    [Fact]
    public void The_factory_registers_a_scheduled_task_on_windows_and_nothing_else()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // The Windows branch of the factory is the only one under test here.
        }

        using var temp = new TempDir();
        Assert.IsType<WindowsTaskAutostart>(AutostartFactory.Create("geovali.exe", temp.Path));
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

    /// <summary>Stands in for the UAC prompt and the elevated copy of GeoVali it starts.</summary>
    private sealed class FakeElevation(bool elevated = true)
    {
        /// <summary>The switch each relaunch was asked to carry.</summary>
        public List<string> Relaunches { get; } = [];

        /// <summary>What the elevated copy exits with.</summary>
        public int ExitCode { get; set; }

        /// <summary>Set to make the prompt itself fail, the way a dismissed UAC dialog does.</summary>
        public Exception? Throws { get; set; }

        public bool IsElevated() => elevated;

        public int Relaunch(string argument)
        {
            Relaunches.Add(argument);
            return Throws is null ? ExitCode : throw Throws;
        }
    }

    private static WindowsTaskAutostart TaskAutostart(FakeSchtasks schtasks, FakeElevation elevation) =>
        new(@"C:\Tools\geovali.exe", schtasks.Run, elevation.IsElevated, elevation.Relaunch);

    [Fact]
    public void Windows_task_is_registered_to_run_at_logon_with_no_window_and_no_password()
    {
        var schtasks = new FakeSchtasks();

        TaskAutostart(schtasks, new FakeElevation(elevated: true)).Enable();

        Assert.Equal(
            ["/create", "/tn", "GeoVali", "/tr", "\"C:\\Tools\\geovali.exe\" --no-browser",
             "/sc", "onlogon", "/np", "/f"],
            Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_reports_enabled_when_the_query_finds_it()
    {
        var schtasks = new FakeSchtasks();

        var enabled = TaskAutostart(schtasks, new FakeElevation()).IsEnabled();

        Assert.True(enabled);
        Assert.Equal(["/query", "/tn", "GeoVali"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_reports_disabled_when_the_query_does_not_find_it()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/query"] = 1 } };

        Assert.False(TaskAutostart(schtasks, new FakeElevation()).IsEnabled());
    }

    [Fact]
    public void Windows_task_is_deleted_without_a_prompt()
    {
        var schtasks = new FakeSchtasks();

        TaskAutostart(schtasks, new FakeElevation(elevated: true)).Disable();

        Assert.Equal(["/delete", "/tn", "GeoVali", "/f"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void Windows_task_delete_tolerates_a_task_that_was_never_there()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/delete"] = 1 } };

        TaskAutostart(schtasks, new FakeElevation(elevated: true)).Disable();
    }

    [Fact]
    public void Windows_task_enable_says_so_when_schtasks_refuses()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/create"] = 1 } };

        Assert.Throws<AutostartException>(
            () => TaskAutostart(schtasks, new FakeElevation(elevated: true)).Enable());
    }

    [Fact]
    public void Enabling_without_administrator_rights_asks_for_them_instead_of_calling_schtasks()
    {
        var schtasks = new FakeSchtasks();
        var elevation = new FakeElevation(elevated: false);

        TaskAutostart(schtasks, elevation).Enable();

        Assert.Equal("--register-autostart", Assert.Single(elevation.Relaunches));
        Assert.Empty(schtasks.Calls);   // a filtered token cannot register a logon trigger at all
    }

    [Fact]
    public void Disabling_without_administrator_rights_asks_for_them_too()
    {
        var schtasks = new FakeSchtasks();
        var elevation = new FakeElevation(elevated: false);

        TaskAutostart(schtasks, elevation).Disable();

        Assert.Equal("--unregister-autostart", Assert.Single(elevation.Relaunches));
        Assert.Empty(schtasks.Calls);
    }

    [Fact]
    public void Declining_the_administrator_prompt_says_so_and_changes_nothing()
    {
        var schtasks = new FakeSchtasks();
        var elevation = new FakeElevation(elevated: false)
        {
            Throws = new Win32Exception(1223)   // ERROR_CANCELLED: the UAC dialog was dismissed
        };

        var error = Assert.Throws<AutostartException>(() => TaskAutostart(schtasks, elevation).Enable());

        Assert.Contains("administrator", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(schtasks.Calls);
    }

    [Fact]
    public void An_elevated_copy_that_fails_is_reported_rather_than_silently_ignored()
    {
        var schtasks = new FakeSchtasks();
        var elevation = new FakeElevation(elevated: false) { ExitCode = 1 };

        Assert.Throws<AutostartException>(() => TaskAutostart(schtasks, elevation).Enable());
    }

    [Fact]
    public void Asking_whether_it_is_enabled_never_needs_administrator_rights()
    {
        var schtasks = new FakeSchtasks();
        var elevation = new FakeElevation(elevated: false);

        Assert.True(TaskAutostart(schtasks, elevation).IsEnabled());

        Assert.Empty(elevation.Relaunches);
        Assert.Equal(["/query", "/tn", "GeoVali"], Assert.Single(schtasks.Calls));
    }

    [Fact]
    public void The_settings_screen_warns_about_the_one_time_administrator_prompt()
    {
        var describe = TaskAutostart(new FakeSchtasks(), new FakeElevation()).Describe();

        Assert.Contains("administrator", describe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_elevated_copy_registers_the_task_and_exits_cleanly()
    {
        var schtasks = new FakeSchtasks();
        var autostart = TaskAutostart(schtasks, new FakeElevation(elevated: true));

        var exitCode = AutostartCommand.Run("--register-autostart", autostart);

        Assert.Equal(0, exitCode);
        Assert.Equal("/create", Assert.Single(schtasks.Calls)[0]);
    }

    [Fact]
    public void The_elevated_copy_removes_the_task_when_asked_to_unregister()
    {
        var schtasks = new FakeSchtasks();
        var autostart = TaskAutostart(schtasks, new FakeElevation(elevated: true));

        var exitCode = AutostartCommand.Run("--unregister-autostart", autostart);

        Assert.Equal(0, exitCode);
        Assert.Equal("/delete", Assert.Single(schtasks.Calls)[0]);
    }

    [Fact]
    public void The_elevated_copy_turns_a_failure_into_a_non_zero_exit_code()
    {
        var schtasks = new FakeSchtasks { ExitCodeByVerb = { ["/create"] = 1 } };
        var autostart = TaskAutostart(schtasks, new FakeElevation(elevated: true));

        Assert.NotEqual(0, AutostartCommand.Run("--register-autostart", autostart));
    }

    [Fact]
    public void The_autostart_switches_are_recognised_and_ordinary_arguments_are_not()
    {
        Assert.True(AutostartCommand.Handles("--register-autostart"));
        Assert.True(AutostartCommand.Handles("--unregister-autostart"));
        Assert.False(AutostartCommand.Handles("--no-browser"));
    }
}
