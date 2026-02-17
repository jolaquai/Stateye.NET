using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Stateye;

/// <summary>
/// Retrieves the .ROBLOSECURITY token from the environment, Windows Credential Manager,
/// or Windows Registry — equivalent to the Rust rbx_cookie crate.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RbxCookie
{
    private const string CookieName = ".ROBLOSECURITY";

    /// <summary>
    /// Attempts to retrieve the Roblox security token using multiple strategies.
    /// </summary>
    public static string GetValue()
    {
        return FromEnvironment()
            ?? FromWindowsCredentials()
            ?? FromWindowsRegistry();
    }

    private static string FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("ROBLOSECURITY");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string FromWindowsCredentials()
    {
        // Try with user ID first
        var userId = CredRead("https://www.roblox.com:RobloxStudioAuthuserid");
        if (userId is not null)
        {
            var cookie = CredRead($"https://www.roblox.com:RobloxStudioAuth{CookieName}{userId}");
            if (cookie is not null)
                return cookie;
        }

        // Legacy credential (no user ID suffix)
        return CredRead($"https://www.roblox.com:RobloxStudioAuth{CookieName}");
    }

    private static string FromWindowsRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Roblox\RobloxStudioBrowser\roblox.com");

        if (key is null)
            return null;

        var value = key.GetValue(CookieName) as string;
        if (value is null)
            return null;

        return ParseRegistryCookie(value);
    }

    /// <summary>
    /// Parses the registry value format: SEC::&lt;YES&gt;,EXP::&lt;...&gt;,COOK::&lt;token&gt;
    /// </summary>
    private static string ParseRegistryCookie(string value)
    {
        foreach (var item in value.Split(','))
        {
            var parts = item.Split("::", 2, StringSplitOptions.None);
            if (parts.Length == 2 && parts[0] == "COOK")
            {
                var cook = parts[1];
                if (cook.StartsWith('<') && cook.EndsWith('>'))
                    return cook[1..^1];
            }
        }

        return null;
    }

    #region Windows Credential Manager P/Invoke

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);

    private static string CredRead(string target)
    {
        const int CRED_TYPE_GENERIC = 1;

        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var credPtr))
            return null;

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlob == 0 || cred.CredentialBlobSize == 0)
                return null;

            return Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    #endregion
}
