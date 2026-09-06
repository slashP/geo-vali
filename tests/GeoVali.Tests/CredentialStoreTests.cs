using System.Text;
using GeoVali.Configuration;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

public class CredentialStoreTests
{
    /// <summary>
    /// Stand-in for DPAPI: reversible, but not plaintext, so a test can prove the cookie is not
    /// written verbatim without depending on which OS the suite runs on.
    /// </summary>
    private sealed class ReversingProtector : ICredentialProtector
    {
        public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext).Reverse().ToArray();
        public string? Unprotect(byte[] protectedBytes) => Encoding.UTF8.GetString(protectedBytes.Reverse().ToArray());
    }

    [Fact]
    public void Round_trips_the_cookie()
    {
        using var temp = new TempDir();

        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("ncfa-value-abc123");

        Assert.Equal("ncfa-value-abc123", new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Never_writes_the_cookie_as_plaintext()
    {
        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("ncfa-value-abc123");

        var onDisk = File.ReadAllText(Path.Combine(temp.Path, CredentialStore.FileName));

        Assert.DoesNotContain("ncfa-value-abc123", onDisk);
    }

    [Fact]
    public void Keeps_the_cookie_out_of_config_json()
    {
        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("secret");
        new ConfigStore(temp.Path).Write(new AppConfig { mapsRoot = Path.Combine(temp.Path, "maps") });

        // A diagnostics dump can safely include config.json; it never touches credentials.json.
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(temp.Path, ConfigStore.FileName)));
        Assert.True(File.Exists(Path.Combine(temp.Path, CredentialStore.FileName)));
    }

    [Fact]
    public void Returns_null_before_a_cookie_has_been_pasted()
    {
        using var temp = new TempDir();
        Assert.Null(new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Clear_removes_the_stored_cookie()
    {
        using var temp = new TempDir();
        var store = new CredentialStore(temp.Path, new ReversingProtector());
        store.WriteCookie("secret");

        store.Clear();

        Assert.Null(store.ReadCookie());
    }

    [Fact]
    public void Returns_null_rather_than_throwing_when_the_stored_value_cannot_be_decoded()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, CredentialStore.FileName), """{"ncfa":"not-base64!!"}""");

        Assert.Null(new CredentialStore(temp.Path, new ReversingProtector()).ReadCookie());
    }

    [Fact]
    public void Restricts_the_credentials_file_to_the_owner_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows relies on DPAPI CurrentUser scope instead of file permissions.
        }

        using var temp = new TempDir();
        new CredentialStore(temp.Path, new ReversingProtector()).WriteCookie("secret");

        var mode = File.GetUnixFileMode(Path.Combine(temp.Path, CredentialStore.FileName));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
