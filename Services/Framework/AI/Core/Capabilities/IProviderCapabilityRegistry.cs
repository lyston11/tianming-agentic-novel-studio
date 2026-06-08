using System.Collections.Generic;

namespace TM.Services.Framework.AI.Core.Capabilities
{
    public interface IProviderCapabilityRegistry
    {
        ProviderCapabilityDescriptor? GetProvider(string? providerId);

        ModelCapabilityDescriptor? GetModel(string? providerId, string? modelId);

        IReadOnlyCollection<string> GetRegisteredProviderIds();
    }
}
