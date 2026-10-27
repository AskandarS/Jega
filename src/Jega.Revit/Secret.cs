using System.Security.Cryptography;
using System.Text;

namespace Jega.Revit;

/// <summary>
/// The Datafordeler key protected for this Windows user (DPAPI), so settings.json doesn't give it away: another user,
/// or the file copied to another PC, can't read it back. Revit's .NET (Windows Desktop) has ProtectedData built in.
/// </summary>
internal static class Secret
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Jega.DatafordelerKey");

    public static string Protect(string plain) =>
        plain.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The key, or "" when there is none or it can't be read here (another user or PC): then it's entered again.</summary>
    public static string Unprotect(string protectedBase64) => TryUnprotect(protectedBase64, out var plain) ? plain : "";

    /// <summary>False when a key is stored but can't be read here (another Windows user, or the file copied from another PC).</summary>
    public static bool TryUnprotect(string protectedBase64, out string plain)
    {
        plain = "";
        if (protectedBase64.Length == 0)
        {
            return true;
        }
        try
        {
            plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
