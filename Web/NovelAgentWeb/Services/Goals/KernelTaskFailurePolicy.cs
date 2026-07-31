using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TM.Web.NovelAgentWeb.Services.Goals;

public enum KernelTaskFailureCategory
{
    Transient,
    NeedsDecision,
    Fatal
}

public enum KernelTaskFailureDisposition
{
    Retry,
    AwaitingDecision,
    FailGoal
}

public sealed record KernelTaskFailureDecision(
    KernelTaskFailureDisposition Disposition,
    TimeSpan RetryAfter);

public static class KernelTaskFailurePolicy
{
    public static KernelTaskFailureCategory Classify(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException or TimeoutException or TaskCanceledException or DbUpdateConcurrencyException ||
                current is NpgsqlException { IsTransient: true })
            {
                return KernelTaskFailureCategory.Transient;
            }
            if (current is ArgumentException or KeyNotFoundException or JsonException)
                return KernelTaskFailureCategory.Fatal;
        }
        return KernelTaskFailureCategory.NeedsDecision;
    }

    public static int MaxAttempts(string taskType) => taskType switch
    {
        "FreezeBaselines" or "CompileChapterContext" or "BatchImpactAnalysis" => 3,
        "CompileBatchPlan" or "PlanChapter" or "WriteCandidate" or "DirectedRework" or
            "ReviewContinuity" or "ReviewLiteraryQuality" or "ExtractContinuitySummary" => 2,
        "PrefixMerge" or "UserAcceptance" => 1,
        _ => 2
    };

    public static KernelTaskFailureDecision Decide(
        string taskType,
        int attempt,
        int maxAttempts,
        KernelTaskFailureCategory category)
    {
        if (category == KernelTaskFailureCategory.Fatal)
            return new KernelTaskFailureDecision(KernelTaskFailureDisposition.FailGoal, TimeSpan.Zero);

        if (category == KernelTaskFailureCategory.Transient &&
            attempt < maxAttempts &&
            taskType is not "PrefixMerge" and not "UserAcceptance")
        {
            var exponent = Math.Clamp(attempt - 1, 0, 5);
            return new KernelTaskFailureDecision(
                KernelTaskFailureDisposition.Retry,
                TimeSpan.FromSeconds(Math.Pow(2, exponent)));
        }

        return new KernelTaskFailureDecision(
            KernelTaskFailureDisposition.AwaitingDecision,
            TimeSpan.Zero);
    }
}
