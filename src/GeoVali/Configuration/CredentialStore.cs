using System.Text.Json;

namespace GeoVali.Configuration;

/// <summary>
/// Holds only the <c>_ncfa</c> cookie, in its own file, so a diagnostics dump of config.json can
/// never contain it.
/// </summary>
public sealed class CredentialStore(string directory, ICredentialProtector protector)
{
    public const string FileName = "credentials.json";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private string FilePath => Path.Combine(directory, FileName);

    private sealed record StoredCredentials(string? ncfa);

    public string? ReadCookie()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(FilePath));
            if (string.IsNullOrEmpty(stored?.ncfa))
            {
                return null;
            }

            var cookie = protector.Unprotect(Convert.FromBase64String(stored.ncfa));
            return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            // Corrupt, or written by another user account. Treat as "no cookie" and let the
            // dashboard ask for a fresh one, rather than failing to start.
            return null;
        }
    }

    public void WriteCookie(string cookie)
    {
        Directory.CreateDirectory(directory);
        var payload = new StoredCredentials(Convert.ToBase64String(protector.Protect(cookie)));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(payload));
        RestrictToOwner();
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    private void RestrictToOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(FilePath, OwnerOnly);
    }
}
