using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TM.Web.NovelAgentWeb.Services.Auth;

public interface ILlmApiKeyProtector
{
    string Protect(string? plaintext);
    string Unprotect(string? protectedValue);
    bool TryUnprotect(string? protectedValue, out string plaintext);
}

public sealed class DataProtectionLlmApiKeyProtector : ILlmApiKeyProtector
{
    private const string Purpose = "NovelAgentWeb.Settings.LlmApiKey.v1";
    private readonly IDataProtector _protector;

    public DataProtectionLlmApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return string.Empty;

        return _protector.Protect(plaintext.Trim());
    }

    public string Unprotect(string? protectedValue)
    {
        return TryUnprotect(protectedValue, out var plaintext) ? plaintext : string.Empty;
    }

    public bool TryUnprotect(string? protectedValue, out string plaintext)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            plaintext = string.Empty;
            return true;
        }

        try
        {
            plaintext = _protector.Unprotect(protectedValue.Trim());
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
    }
}
