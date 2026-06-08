using System;

namespace TM.Services.Framework.AI.SemanticKernel.Discovery
{
    public sealed record DiscoveryRecord<T>(T Value, DiscoverySource Source, DateTime Timestamp)
        where T : struct, IComparable<T>;
}
