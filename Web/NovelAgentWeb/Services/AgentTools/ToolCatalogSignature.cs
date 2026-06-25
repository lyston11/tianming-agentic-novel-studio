using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public static class ToolCatalogSignature
{
    public static string Compute(IEnumerable<ToolSchema> tools)
    {
        var canonical = tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new
            {
                tool.Name,
                tool.Description,
                tool.Risk,
                tool.RequiresConfirmation,
                Parameters = tool.Parameters
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                SideEffects = new
                {
                    tool.SideEffects.WritesLedger,
                    tool.SideEffects.WritesRedisRecentCache,
                    tool.SideEffects.WritesToolSearchCache,
                    tool.SideEffects.WritesSqliteSnapshot,
                    tool.SideEffects.BusinessReadOnly,
                    WritesMemoryScopes = Sorted(tool.SideEffects.WritesMemoryScopes),
                    ReadsSqliteEntities = Sorted(tool.SideEffects.ReadsSqliteEntities),
                    WritesSqliteEntities = Sorted(tool.SideEffects.WritesSqliteEntities),
                    WritesVectorIndexes = Sorted(tool.SideEffects.WritesVectorIndexes)
                },
                Semantic = new
                {
                    tool.Semantic.DisplayName,
                    tool.Semantic.DomainSurface,
                    tool.Semantic.OutputKind,
                    tool.Semantic.SideEffectLevel,
                    tool.Semantic.ImpactScope,
                    tool.Semantic.FailureContract,
                    tool.Semantic.RequiresProject,
                    tool.Semantic.SupportsNoProjectSession,
                    tool.Semantic.AverageDuration,
                    ProgressEventContract = Sorted(tool.Semantic.ProgressEventContract),
                    NextPossibleTools = Sorted(tool.Semantic.NextPossibleTools),
                    ReadsFrom = Sorted(tool.Semantic.ReadsFrom),
                    WritesTo = Sorted(tool.Semantic.WritesTo),
                    InputArtifacts = Sorted(tool.Semantic.InputArtifacts),
                    OutputArtifacts = Sorted(tool.Semantic.OutputArtifacts),
                    tool.Semantic.IdempotencyPolicy,
                    tool.Semantic.RollbackPolicy,
                    tool.Semantic.UserVisibleWhere,
                    tool.Semantic.ResultSemantics
                }
            })
            .ToList();

        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
}
