using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TM.Web.NovelAgentWeb.Services.Auth;

public interface ILlmApiKeyProtector
{
    string Protect(string? plaintext);
    string Unprotect(string? protectedValue);
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
        if (string.IsNullOrWhiteSpace(protectedValue))
            return string.Empty;

        try
        {
            return _protector.Unprotect(protectedValue.Trim());
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }
}
