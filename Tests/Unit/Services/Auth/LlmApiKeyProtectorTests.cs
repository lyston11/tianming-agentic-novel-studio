using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace Tests.Unit.Services.Auth;

public sealed class LlmApiKeyProtectorTests
{
    [Fact]
    public void Protect_ReturnsOpaqueValueAndUnprotectRestoresPlaintext()
    {
        ILlmApiKeyProtector protector = new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider());

        var protectedValue = protector.Protect("sk-unit-test-secret");

        Assert.NotEqual("sk-unit-test-secret", protectedValue);
        Assert.Equal("sk-unit-test-secret", protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_EmptyValuesRemainEmpty()
    {
        ILlmApiKeyProtector protector = new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider());

        Assert.Equal(string.Empty, protector.Protect(""));
        Assert.Equal(string.Empty, protector.Unprotect(null));
    }

    [Fact]
    public void Unprotect_InvalidPayloadReturnsEmptyValue()
    {
        ILlmApiKeyProtector protector = new DataProtectionLlmApiKeyProtector(new EphemeralDataProtectionProvider());

        Assert.Equal(string.Empty, protector.Unprotect("not-a-valid-protected-payload"));
    }
}
