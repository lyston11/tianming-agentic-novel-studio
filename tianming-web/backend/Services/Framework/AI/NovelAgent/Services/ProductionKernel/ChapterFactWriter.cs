using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterFactWriter : IChapterFactWriter
    {
        public async Task<ChapterFactWriteResult> ExtractAndPersistAsync(
            ChapterFactWriteRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.CommittedContent))
            {
                return new ChapterFactWriteResult
                {
                    Success = false,
                    Message = "章节正文为空，跳过连续性事实沉淀。"
                };
            }

            if (request.CompleteAsync == null)
            {
                return new ChapterFactWriteResult
                {
                    Success = false,
                    Message = "未提供章节事实抽取模型调用器。"
                };
            }

            if (request.PersistAsync == null)
            {
                return new ChapterFactWriteResult
                {
                    Success = false,
                    Message = "未提供章节事实持久化写入器。"
                };
            }

            var raw = await request.CompleteAsync(
                    BuildContinuityExtractionSystemPrompt(),
                    BuildContinuityExtractionUserPrompt(request.Run, request.ContextPackage, request.CommittedContent),
                    ct)
                .ConfigureAwait(false);

            if (!TryDeserializeContinuityFacts(raw, out var facts))
            {
                return new ChapterFactWriteResult
                {
                    Success = false,
                    Message = "LLM 连续性事实 JSON 不可解析，已跳过沉淀；不会使用规则抽取伪造事实。"
                };
            }

            facts.ChapterId = FirstNonEmpty(facts.ChapterId, request.Run.TargetChapterId, request.ContextPackage.ChapterId);
            facts.SourceRunId = FirstNonEmpty(facts.SourceRunId, request.Run.RunId);
            facts.ExtractedAt = DateTime.Now;

            var commit = await request.PersistAsync(facts, ct).ConfigureAwait(false);
            return new ChapterFactWriteResult
            {
                Success = commit.Success,
                Message = commit.Message,
                Facts = facts,
                CommitResult = commit
            };
        }

        private static string BuildContinuityExtractionSystemPrompt() =>
            """
            你是长篇小说事实沉淀模型。只从给定成稿中抽取已经发生且明确写出的事实，不推测、不补设定。
            必须只输出一个合法 JSON 对象，不要 Markdown，不要解释。
            JSON 字段必须包含：
            chapterId, chapterTitle, protagonistName, protagonistIdentity, protagonistStatus, currentLocation,
            systemState, equipmentState, keyEvents, endingState, nextChapterMustCarry。
            keyEvents 和 nextChapterMustCarry 必须是字符串数组；没有明确事实时填空字符串或空数组。
            """;

        private static string BuildContinuityExtractionUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            string committedContent)
        {
            var payload = new
            {
                task = "extract_chapter_continuity_facts",
                chapterId = run.TargetChapterId,
                existingHardContinuityFacts = contextPackage.HardContinuityFacts,
                chapterText = committedContent
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static bool TryDeserializeContinuityFacts(string raw, out ChapterContinuityFacts facts)
        {
            facts = new ChapterContinuityFacts();
            var json = ExtractJsonObject(raw);
            if (!HasText(json))
                return false;

            try
            {
                facts = JsonSerializer.Deserialize<ChapterContinuityFacts>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new ChapterContinuityFacts();
                return HasText(facts.ChapterId) ||
                       HasText(facts.ProtagonistName) ||
                       HasText(facts.EndingState) ||
                       facts.KeyEvents.Count > 0 ||
                       facts.NextChapterMustCarry.Count > 0;
            }
            catch
            {
                facts = new ChapterContinuityFacts();
                return false;
            }
        }

        private static string ExtractJsonObject(string raw)
        {
            return JsonObjectTextExtractor.ExtractFirstObjectOrEmpty(raw);
        }

        private static bool HasText(string? value) =>
            !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;
    }
}
