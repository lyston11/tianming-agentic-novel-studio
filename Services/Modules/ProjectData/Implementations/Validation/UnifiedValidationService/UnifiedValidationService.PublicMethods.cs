using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 公开方法

        public async Task<bool> NeedsRepublishAsync()
        {
            var manifest = await _publishService.GetManifestAsync().ConfigureAwait(false);
            if (manifest == null) return true;
            return _publishService.NeedsRepublish();
        }

        public async Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default)
        {
            var chapterContent = await _contentService.GetChapterAsync(chapterId).ConfigureAwait(false);
            return await ValidateChapterInternalAsync(chapterId, chapterContent, ct).ConfigureAwait(false);
        }

        public Task<ChapterValidationResult> ValidateChapterWithContentAsync(string chapterId, string chapterContent, CancellationToken ct = default)
        {
            return ValidateChapterInternalAsync(chapterId, chapterContent, ct);
        }

        private async Task<ChapterValidationResult> ValidateChapterInternalAsync(string chapterId, string? chapterContent, CancellationToken ct = default)
        {
            TM.App.Log($"[UnifiedValidationService] 开始校验章节: {chapterId}");

            await EnsurePackagedDataOrThrowAsync().ConfigureAwait(false);

            var (volumeNumber, chapterNumber) = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
            if (volumeNumber == 0 || chapterNumber == 0)
            {
                TM.App.Log($"[UnifiedValidationService] 无法解析章节ID: {chapterId}");
                return CreateErrorResult(chapterId, "无法解析章节ID");
            }

            if (string.IsNullOrEmpty(chapterContent))
            {
                var volumeNameForError = await GetVolumeNameAsync(volumeNumber).ConfigureAwait(false);
                TM.App.Log($"[UnifiedValidationService] 章节正文不存在: {chapterId}");
                return CreateErrorResult(chapterId, "章节正文不存在", volumeNumber, chapterNumber, volumeNameForError);
            }

            var volumeName = await GetVolumeNameAsync(volumeNumber).ConfigureAwait(false);

            var chapterTitle = ExtractChapterTitle(chapterContent);

            var context = await _contextService.GetValidationContextAsync(chapterId).ConfigureAwait(false);
            if (context == null)
            {
                TM.App.Log($"[UnifiedValidationService] 无法加载校验上下文: {chapterId}");
                return CreateErrorResult(chapterId, "无法加载校验上下文，请先执行打包", volumeNumber, chapterNumber, volumeName);
            }

            var contentGuide = await _guideContextService.GetContentGuideAsync().ConfigureAwait(false);
            if (contentGuide?.Chapters?.TryGetValue(chapterId, out var guideEntry) != true || guideEntry == null)
            {
                var errorMsg = $"ContextIds 缺失：章节 {chapterId} 未写入指导文件，请重新打包/更新。";
                TM.App.Log($"[UnifiedValidationService] {errorMsg}");
                return CreateErrorResult(chapterId, errorMsg, volumeNumber, chapterNumber, volumeName);
            }

            var contextIdsValidation = await _guideContextService.ValidateContextIdsAsync(guideEntry.ContextIds).ConfigureAwait(false);
            if (!contextIdsValidation.IsValid)
            {
                var errorMsg = $"ContextIds 解析失败，索引与本体不一致，请重新打包/更新。{Environment.NewLine}{contextIdsValidation.GetErrorSummary()}";
                TM.App.Log($"[UnifiedValidationService] {errorMsg}");
                return CreateErrorResult(chapterId, errorMsg, volumeNumber, chapterNumber, volumeName);
            }

            var result = new ChapterValidationResult
            {
                ChapterId = chapterId,
                ChapterTitle = chapterTitle,
                VolumeNumber = volumeNumber,
                ChapterNumber = chapterNumber,
                VolumeName = volumeName,
                ValidatedTime = DateTime.Now
            };

            ct.ThrowIfCancellationRequested();

            await RunGateChecksAsync(result, chapterId, chapterContent, guideEntry.ContextIds).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            await ExecuteValidationsAsync(result, context, chapterContent, ct).ConfigureAwait(false);

            result.OverallResult = DetermineOverallResult(result);

            TM.App.Log($"[UnifiedValidationService] 章节校验完成: {chapterId}, 结果: {result.OverallResult}, 问题数: {result.TotalIssueCount}");
            return result;
        }

        public async Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default)
        {
            TM.App.Log($"[UnifiedValidationService] 开始校验第{volumeNumber}卷");

            await EnsurePackagedDataOrThrowAsync().ConfigureAwait(false);

            var volumeName = await GetVolumeNameAsync(volumeNumber).ConfigureAwait(false);
            var result = new VolumeValidationResult
            {
                VolumeNumber = volumeNumber,
                VolumeName = volumeName,
                ValidatedTime = DateTime.Now
            };

            var chapters = await _contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
            var volumeChapters = chapters.Where(c => c.VolumeNumber == volumeNumber)
                                         .OrderBy(c => c.ChapterNumber)
                                         .ToList();

            TM.App.Log($"[UnifiedValidationService] 第{volumeNumber}卷共{volumeChapters.Count}个章节");

            if (volumeChapters.Count == 0)
            {
                TM.App.Log($"[UnifiedValidationService] 第{volumeNumber}卷没有章节，跳过校验");
                return result;
            }

            var sampleCount = CalculateSampleCount(volumeChapters.Count);
            var sampledChapters = SampleChapters(volumeChapters, sampleCount);
            TM.App.Log($"[UnifiedValidationService] 卷章节总数: {volumeChapters.Count}, 抽样章节数: {sampledChapters.Count}");

            const int maxConcurrency = 8;
            using var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency, maxConcurrency);

            var batches = sampledChapters
                .Select((ch, i) => new { ch, i })
                .GroupBy(x => x.i / ValidationBatchSize)
                .Select(g => g.Select(x => x.ch).ToList())
                .ToList();

            var batchTasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (batch.Count == 1)
                        return new[] { await ValidateChapterAsync(batch[0].Id, ct).ConfigureAwait(false) };
                    return await ValidateChapterBatchAsync(batch, volumeName, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[UnifiedValidationService] 批次校验失败: {string.Join(",", batch.Select(c => c.Id))}, {ex.Message}");
                    return batch.Select(c => CreateErrorResult(c.Id, $"校验异常: {ex.Message}", c.VolumeNumber, c.ChapterNumber, volumeName)).ToArray();
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var chapterResults = (await Task.WhenAll(batchTasks).ConfigureAwait(false))
                .SelectMany(r => r)
                .OrderBy(r => r.ChapterNumber)
                .ToList();

            foreach (var r in chapterResults)
            {
                result.ChapterResults.Add(r);
            }

            var summaryData = AggregateToVolumeSummary(volumeNumber, volumeName, sampledChapters, chapterResults);

            _validationSummaryService.SaveVolumeValidation(volumeNumber, summaryData);

            TM.App.Log($"[UnifiedValidationService] 卷校验完成: 第{volumeNumber}卷, 抽样: {sampledChapters.Count}章, 结果: {summaryData.OverallResult}");
            return result;
        }

        private async Task<ChapterValidationResult[]> ValidateChapterBatchAsync(List<ChapterInfo> batch, string volumeName, CancellationToken ct = default)
        {
            var results = new List<ChapterValidationResult>();
            var contents = new List<string?>();

            var fetchedContents = await Task.WhenAll(batch.Select(c => _contentService.GetChapterAsync(c.Id))).ConfigureAwait(false);

            for (var i = 0; i < batch.Count; i++)
            {
                var chapter = batch[i];
                var content = fetchedContents[i];

                if (string.IsNullOrEmpty(content))
                {
                    results.Add(CreateErrorResult(chapter.Id, "章节正文不存在", chapter.VolumeNumber, chapter.ChapterNumber, volumeName));
                    contents.Add(null);
                    continue;
                }

                var chapterTitle = ExtractChapterTitle(content);
                var r = new ChapterValidationResult
                {
                    ChapterId = chapter.Id,
                    ChapterTitle = chapterTitle,
                    VolumeNumber = chapter.VolumeNumber,
                    ChapterNumber = chapter.ChapterNumber,
                    VolumeName = volumeName,
                    ValidatedTime = DateTime.Now
                };
                results.Add(r);
                contents.Add(content);
            }

            var pendingIndices = contents
                .Select((c, i) => (c, i))
                .Where(x => x.c != null)
                .Select(x => x.i)
                .ToList();

            if (pendingIndices.Count == 0)
                return results.ToArray();

            ct.ThrowIfCancellationRequested();

            if (pendingIndices.Count == 1)
            {
                var idx = pendingIndices[0];
                await ExecuteValidationsAsync(results[idx], await _contextService.GetValidationContextAsync(batch[idx].Id), contents[idx]!, ct).ConfigureAwait(false);
                results[idx].OverallResult = DetermineOverallResult(results[idx]);
                TM.App.Log($"[UnifiedValidationService] 单章校验完成: {batch[idx].Id}, 结果: {results[idx].OverallResult}");
                return results.ToArray();
            }

            var batchPrompt = await BuildBatchValidationPromptAsync(batch, pendingIndices, contents, results).ConfigureAwait(false);
            var aiResult = await _aiService.GenerateAsync(batchPrompt, ct).ConfigureAwait(false);

            if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
            {
                TM.App.Log($"[UnifiedValidationService] 批量AI校验失败，降级逐章: {aiResult.ErrorMessage}");
                foreach (var idx in pendingIndices)
                    await ExecuteValidationsAsync(results[idx], await _contextService.GetValidationContextAsync(batch[idx].Id), contents[idx]!, ct).ConfigureAwait(false);
            }
            else
            {
                ParseBatchAIValidationResult(pendingIndices.Select(i => results[i]).ToList(), batch, aiResult.Content);
            }

            foreach (var idx in pendingIndices)
            {
                results[idx].OverallResult = DetermineOverallResult(results[idx]);
                TM.App.Log($"[UnifiedValidationService] 批量校验完成: {batch[idx].Id}, 结果: {results[idx].OverallResult}");
            }

            return results.ToArray();
        }

        private async Task<string> BuildBatchValidationPromptAsync(
            List<ChapterInfo> batch, List<int> pendingIndices,
            List<string?> contents, List<ChapterValidationResult> results)
        {
            var sb = new StringBuilder();

            var templatePrompt = GetValidationTemplateSystemPrompt();
            if (!string.IsNullOrWhiteSpace(templatePrompt))
            {
                sb.AppendLine("<validation_system_prompt>");
                sb.AppendLine(templatePrompt);
                sb.AppendLine("</validation_system_prompt>");
                sb.AppendLine();
            }

            sb.AppendLine("<batch_validation_task>");
            sb.AppendLine($"<batch_size>{pendingIndices.Count}</batch_size>");
            sb.AppendLine("请对以下每个章节分别执行校验，返回JSON数组，数组长度必须严格等于 batch_size，第i项对应第i个章节。");
            sb.AppendLine();

            for (int seq = 0; seq < pendingIndices.Count; seq++)
            {
                var idx = pendingIndices[seq];
                var r = results[idx];
                var content = contents[idx]!;
                sb.AppendLine($"<chapter index=\"{seq + 1}\">");
                sb.AppendLine($"<chapter_id>{r.ChapterId}</chapter_id>");
                sb.AppendLine($"<chapter_info>标题={r.ChapterTitle}, 卷={r.VolumeNumber}, 章={r.ChapterNumber}</chapter_info>");

                var contentGuide = await _guideContextService.GetContentGuideAsync().ConfigureAwait(false);
                contentGuide.Chapters.TryGetValue(r.ChapterId, out var guideEntry);
                var contextIds = guideEntry?.ContextIds;
                if (contextIds != null)
                {
                    var charsTask = contextIds.Characters?.Count > 0
                        ? _guideContextService.ExtractCharactersAsync(contextIds.Characters)
                        : Task.FromResult(new List<Models.Design.Characters.CharacterRulesData>());
                    var factionsTask = contextIds.Factions?.Count > 0
                        ? _guideContextService.ExtractFactionsAsync(contextIds.Factions)
                        : Task.FromResult(new List<Models.Design.Factions.FactionRulesData>());
                    var plotsTask = contextIds.PlotRules?.Count > 0
                        ? _guideContextService.ExtractPlotRulesAsync(contextIds.PlotRules)
                        : Task.FromResult(new List<Models.Design.Plot.PlotRulesData>());
                    await Task.WhenAll(charsTask, factionsTask, plotsTask).ConfigureAwait(false);

                    var chars = await charsTask.ConfigureAwait(false);
                    if (chars.Count > 0)
                        sb.AppendLine($"<characters>{string.Join("; ", chars.Take(5).Select(c => $"{c.Name}({c.Identity})"))}</characters>");
                    var factions = await factionsTask.ConfigureAwait(false);
                    if (factions.Count > 0)
                        sb.AppendLine($"<factions>{string.Join("; ", factions.Take(5).Select(f => f.Name))}</factions>");
                    var plots = await plotsTask.ConfigureAwait(false);
                    if (plots.Count > 0)
                        sb.AppendLine($"<plot_rules>{string.Join("; ", plots.Take(3).Select(p => $"{p.Name}:{TruncateString(p.Goal, 30)}"))}</plot_rules>");
                }
                sb.AppendLine($"<正文内容>{(content.Length > ChapterPreviewLength ? content.Substring(0, ChapterPreviewLength) + "..." : content)}</正文内容>");
                sb.AppendLine("</chapter>");
                sb.AppendLine();
            }

            sb.AppendLine("<校验要求>");
            sb.AppendLine($"对每个章节执行{ValidationRules.TotalRuleCount}条校验规则，返回JSON数组，数组长度={pendingIndices.Count}，顺序与输入章节一致：");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"chapterId\": \"章节ID\",");
            sb.AppendLine("    \"overallResult\": \"通过|警告|失败|未校验\",");
            sb.AppendLine("    \"moduleResults\": " + BuildJsonTemplateForPrompt().Replace("\n", "\n    ").TrimEnd());
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine($"每个对象的 moduleResults 必须包含全部 {ValidationRules.TotalRuleCount} 条规则，moduleName 必须与模板一致，不得为 null 或省略。");
            sb.AppendLine($"重要：summary、reason、suggestion 字段中不得引用提示词中的标签名称（如正文内容、缺失数据说明等），只描述内容本身。");
            sb.AppendLine("</校验要求>");
            sb.AppendLine("</batch_validation_task>");
            return sb.ToString();
        }

        private void ParseBatchAIValidationResult(List<ChapterValidationResult> results, List<ChapterInfo> batch, string aiContent)
        {
            try
            {
                var arrStart = aiContent.IndexOf('[');
                var arrEnd = aiContent.LastIndexOf(']');
                if (arrStart < 0 || arrEnd <= arrStart)
                {
                    TM.App.Log("[UnifiedValidationService] 批量校验：AI返回中未找到JSON数组，降级逐项处理");
                    foreach (var r in results)
                        AddProtocolErrorIssue(r, "批量校验AI返回格式错误");
                    return;
                }

                var jsonStr = aiContent.Substring(arrStart, arrEnd - arrStart + 1);
                var arr = JsonDocument.Parse(jsonStr).RootElement;
                var elements = arr.EnumerateArray().ToList();

                for (int i = 0; i < results.Count; i++)
                {
                    if (i >= elements.Count)
                    {
                        AddProtocolErrorIssue(results[i], "批量校验AI返回数组长度不足");
                        continue;
                    }
                    var elem = elements[i];
                    if (elem.TryGetProperty("moduleResults", out var moduleResultsArray))
                        ParseNewProtocolResult(results[i], moduleResultsArray);
                    else
                        AddProtocolErrorIssue(results[i], "批量校验结果缺少moduleResults");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 批量校验结果解析失败: {ex.Message}");
                foreach (var r in results)
                    AddProtocolErrorIssue(r, $"批量解析失败: {ex.Message}");
            }
        }

        #endregion
    }
}
