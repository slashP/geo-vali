using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace GeoVali.Configuration;

public interface ICredentialProtector
{
    byte[] Protect(string plaintext);
    string? Unprotect(byte[] protectedBytes);
}

/// <summary>Windows: DPAPI, CurrentUser scope. The bytes are useless to any other account.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    public byte[] Protect(string plaintext) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public string? Unprotect(byte[] protectedBytes)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // Written by a different Windows account, or the profile was rebuilt.
            return null;
        }
    }
}

/// <summary>
/// macOS and Linux: no encryption, only the owner-only file permissions applied by
/// <see cref="CredentialStore"/>. This asymmetry is deliberate and is stated to the user in the
/// README and the settings screen. An OS keychain would cost a native dependency per platform,
/// which is not worth it for a session cookie the user can revoke by signing out of GeoGuessr.
/// </summary>
public sealed class PassthroughCredentialProtector : ICredentialProtector
{
    public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext);
    public string? Unprotect(byte[] protectedBytes) => Encoding.UTF8.GetString(protectedBytes);
}

public static class CredentialProtectorFactory
{
    public static ICredentialProtector Create() =>
        OperatingSystem.IsWindows() ? new DpapiCredentialProtector() : new PassthroughCredentialProtector();
}
