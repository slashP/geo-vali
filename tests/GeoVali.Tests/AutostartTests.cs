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
        var autostart = new WindowsAutostart(@"C:\Users\perhel\.dotnet\tools\geovali.exe", temp.Path);

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

        Assert.Contains("Startup folder", new WindowsAutostart("v", temp.Path).Describe());
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
}
