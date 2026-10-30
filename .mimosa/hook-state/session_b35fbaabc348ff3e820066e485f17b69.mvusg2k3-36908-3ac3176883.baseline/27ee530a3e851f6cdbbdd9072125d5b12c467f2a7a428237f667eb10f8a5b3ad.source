using System.Security.Cryptography;
using System.Text;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Configuration;

public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    public string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("本地凭据需要 Windows 当前用户的 DPAPI。");
        }

        var plaintext = Utf8.GetBytes(secret);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("本地凭据需要 Windows 当前用户的 DPAPI。");
        }

        byte[] encrypted;
        try
        {
            encrypted = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("本地凭据格式无效。", exception);
        }

        var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            return Utf8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
