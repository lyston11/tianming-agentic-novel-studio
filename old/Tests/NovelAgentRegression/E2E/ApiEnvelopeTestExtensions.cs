using System.Net.Http.Json;
using System.Text.Json;
using TM.Web.NovelAgentWeb.DTOs;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

internal static class ApiEnvelopeTestExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T> ReadEnvelopeDataAsync<T>(this HttpContent content)
    {
        var envelope = await content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Error?.Message ?? "Expected successful API envelope.");
        Assert.NotNull(envelope.Data);
        return envelope.Data!;
    }
}
