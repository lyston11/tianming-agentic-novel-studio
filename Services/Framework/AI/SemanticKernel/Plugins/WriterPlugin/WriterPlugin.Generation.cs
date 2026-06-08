using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Modules.Generate.Content.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using System.Reflection;
using TM.Services.Modules.ProjectData.Models.TaskContexts;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class WriterPlugin
    {
        [KernelFunction("GenerateChapter")]
        [Description("根据章节ID对应的打包数据和项目设定生成完整章节内容，并保存到当前项目的章节文件中。")]
        public async Task<string> GenerateChapterAsync(
            CancellationToken ct,
            [Description("章节ID，如 vol1_ch1。留空时根据当前启用的卷自动生成。")] string chapterId = "",
            [Description("写作风格要求，如'轻松幽默'，可选")] string style = "",
            [Description("目标字数，例如 3500，0 表示不强制")] int wordCount = 0)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapter: {chapterId}");

            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    const string msg = "[生成失败] 未指定章节ID。请使用 @chapter:volN_chM / @重写:volN_chM / @续写:volN_chM，或输入“第N卷第M章/第M章”（需分卷设计配置章节范围）。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                await EnsureVolumeExistsForRewriteAsync(ct, chapterId).ConfigureAwait(false);

                var _kfParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_kfParsed.HasValue && _kfParsed.Value.chapterNumber == 1 && _kfParsed.Value.volumeNumber > 1)
                {
                    var _kfPrevVol = _kfParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _kfArchiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                        var _kfPrevArchives = await _kfArchiveStore.GetPreviousArchivesAsync(_kfParsed.Value.volumeNumber).ConfigureAwait(false);
                        if (!_kfPrevArchives.Any(a => a.VolumeNumber == _kfPrevVol))
                        {
                            TM.App.Log($"[WriterPlugin] 检测到新卷第1章，自动存档第{_kfPrevVol}卷...");
                            var _kfReconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                            await _kfReconciler.AutoArchiveVolumeIfNeededAsync(_kfPrevVol).ConfigureAwait(false);
                            TM.App.Log($"[WriterPlugin] 第{_kfPrevVol}卷自动存档完成");
                        }
                    }
                    catch (Exception _kfArchiveEx)
                    {
                        TM.App.Log($"[WriterPlugin] 第{_kfPrevVol}卷自动存档失败（不阻断生成）: {_kfArchiveEx.Message}");
                    }
                }

                var contentServiceF2 = ServiceLocator.Get<GeneratedContentService>();
                if (contentServiceF2.ChapterExists(chapterId))
                {
                    var dupMsg = $"章节 {chapterId} 已存在。如需重新生成请使用 @重写:{chapterId} 指令。";
                    TM.App.Log($"[WriterPlugin] 重复生成拦截: {dupMsg}");
                    return dupMsg;
                }

                await EnsureSequentialChapterContinuityAsync(ct, chapterId).ConfigureAwait(false);

                TM.App.Log($"[WriterPlugin] 开始生成章节: {chapterId}");
                GenerationProgressHub.ReportPhase(ProgressPhase.Preparing, $"正在准备生成 {chapterId}：加载打包上下文...");

                var specTask = ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();

                var guideService = ServiceLocator.Get<GuideContextService>();
                var ctx = await guideService.BuildContentContextAsync(chapterId, ct).ConfigureAwait(false);
                if (ctx == null)
                {
                    var msg = $"[生成失败] 无法获取章节 {chapterId} 的打包上下文，请确认已执行打包。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot == null)
                {
                    var msg = $"[生成失败] 章节 {chapterId} 为正式版上下文（打包+MD），但 FactSnapshot 缺失。请重新打包或修复账本后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    return msg;
                }

                GenerationProgressHub.Report("正在并行加载前置数据...");
                using var vectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var vectorTask = PopulateLongDistanceRecallAsync(ctx, vectorCts.Token);

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot != null)
                {
                    var publishService = ServiceLocator.Get<IPublishService>();
                    var manifest = await publishService.GetManifestAsync().ConfigureAwait(false);

                    var changeDetection = ServiceLocator.Get<IChangeDetectionService>();
                    GenerationProgressHub.Report("正在校验打包一致性：扫描模块变更...");
                    await changeDetection.RefreshAllAsync().ConfigureAwait(false);
                    var configService = ServiceLocator.Get<ContentConfigService>();

                    var enabledChangedModules = changeDetection.GetChangedModules()
                        .Where(m => configService.IsModuleEnabled(m))
                        .ToList();

                    if (enabledChangedModules.Count > 0)
                    {
                        var msg = "[生成失败] 检测到已启用模块存在未打包变更，正式版生成条件未满足。请先重新打包后重试。\n" +
                                  string.Join("\n", enabledChangedModules.Select(m =>
                                      "- " + m.Replace("Generate", "生成")
                                              .Replace("Design", "设计")
                                              .Replace("GlobalSettings", "全局设置")
                                              .Replace("Elements", "元素")));
                        TM.App.Log($"[WriterPlugin] {msg}");
                        vectorCts.Cancel();
                        return msg;
                    }

                    var contextIdsValidation = await guideService.ValidateContextIdsAsync(ctx.ContextIds).ConfigureAwait(false);
                    if (!contextIdsValidation.IsValid)
                    {
                        var msg = $"[生成失败] ContextIds 解析失败，索引与本体不一致。\n{contextIdsValidation.GetErrorSummary()}";
                        TM.App.Log($"[WriterPlugin] {msg}");
                        vectorCts.Cancel();
                        return msg;
                    }
                }

                var projectSpec = await specTask.ConfigureAwait(false);
                CreativeSpec? overrideSpec = null;
                if (!string.IsNullOrWhiteSpace(style) || wordCount > 0)
                {
                    overrideSpec = new CreativeSpec();
                    if (!string.IsNullOrWhiteSpace(style))
                    {
                        overrideSpec.WritingStyle = style;
                    }

                    if (wordCount > 0)
                    {
                        overrideSpec.TargetWordCount = wordCount;
                    }
                }

                var effectiveSpec = CreativeSpec.Merge(projectSpec, overrideSpec);
                await vectorTask.ConfigureAwait(false);

                string rawContent;

                if (ctx.FactSnapshot == null && ctx.ContextMode != ContentContextMode.Full)
                {
                    var msg = $"[生成失败] {chapterId} 缺少 FactSnapshot，强一致模式下禁止轻量生成/跳过一致性校验。请先完成打包后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    return msg;
                }

                TM.App.Log($"[WriterPlugin] gen: {chapterId}");
                GenerationProgressHub.Report("准备完成，开始正式生成章节...");
                var genResult = await ServiceLocator.Get<AutoRewriteEngine>().GenerateWithRewriteAsync(
                    chapterId,
                    ctx,
                    ctx.FactSnapshot!,
                    effectiveSpec,
                    ct).ConfigureAwait(false);

                if (!genResult.Success)
                {
                    if (genResult.RequiresManualIntervention)
                    {
                        TM.App.Log($"[WriterPlugin] 需要人工介入: {chapterId}");
                        return $"[生成失败] {genResult.InterventionHint}\n\n最后失败原因：\n{string.Join("\n", genResult.GetLastFailureReasons().Select(f => $"- {f}"))}";
                    }

                    var error = string.IsNullOrWhiteSpace(genResult.ErrorMessage)
                        ? "[生成失败] AI 未返回任何内容"
                        : $"[生成失败] {genResult.ErrorMessage}";
                    TM.App.Log($"[WriterPlugin] {error}");
                    return error;
                }

                rawContent = genResult.Content!;
                TM.App.Log($"[WriterPlugin] gen ok: {chapterId}, attempts: {genResult.TotalAttempts}");

                var cleaned = StripLeadingTitle(
                    StripPromptEchoKeepChanges(CleanContentKeepChanges(rawContent), ctx.Title));

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                var effectiveSnapshot2 = ctx.FactSnapshot ?? await TryBuildLazySnapshotAsync(chapterId, ctx.ContextIds).ConfigureAwait(false);
                if (effectiveSnapshot2 != null)
                {
                    await callback.OnContentGeneratedStrictAsync(
                        chapterId,
                        cleaned,
                        effectiveSnapshot2,
                        genResult.GateResult,
                        genResult.DesignElements,
                        ctx.ContextIds).ConfigureAwait(false);
                }
                else
                {
                    var msg = $"[生成失败] {chapterId} FactSnapshot不可用，强一致模式下禁止降级为非严格保存。请检查打包上下文是否完整后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    return msg;
                }

                var cleanedBody = GenerationGate.StripChangesSection(cleaned);
                var actualWordCount = CountWords(cleanedBody);
                var title = string.IsNullOrWhiteSpace(ctx.Title) ? chapterId : ctx.Title;

                TM.App.Log($"[WriterPlugin] 章节生成并保存成功: {chapterId}, 标题: {title}, 字数: {actualWordCount}");
                GlobalToast.Success("章节已保存", $"「{title}」约 {actualWordCount} 字");

                var persisted = await ServiceLocator.Get<GeneratedContentService>().GetChapterAsync(chapterId).ConfigureAwait(false);
                var displayContent = persisted ?? cleaned;

                CurrentChapterTracker.SetCurrentChapter(chapterId, title);
                await TryAutoSwitchVolumeAfterGenerationAsync(chapterId).ConfigureAwait(false);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Comm.PublishRefreshChapterList();
                    Comm.PublishChapterSelected(chapterId, BuildCanonicalTabTitle(chapterId, title), displayContent);
                    StandardDialog.FlashTaskbarIfBackground(System.Windows.Application.Current.MainWindow);
                });

                return $"已生成章节「{title}」，约 {actualWordCount} 字。\n内容已保存到项目章节文件（{chapterId}.md），对话中不展示完整正文。";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 生成已取消");
                return "[已取消] 生成被用户取消";
            }
            catch (PolishFatalException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                return $"[异常] {ex.Message}";
            }
        }

        public async Task<string> GenerateChapterAsync(CancellationToken ct, string chapterId = "")
        {
            TM.App.Log($"[WriterPlugin] GenerateChapter (可取消): {chapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(chapterId))
                {
                    throw new InvalidOperationException("未指定章节ID。请使用 @chapter:volN_chM / @重写:volN_chM / @续写:volN_chM，或输入“第N卷第M章/第M章”（需分卷设计配置章节范围）。");
                }

                await EnsureVolumeExistsForRewriteAsync(ct, chapterId).ConfigureAwait(false);

                var _autoArchiveParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_autoArchiveParsed.HasValue && _autoArchiveParsed.Value.chapterNumber == 1 && _autoArchiveParsed.Value.volumeNumber > 1)
                {
                    var _prevVol = _autoArchiveParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _archiveStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
                        var _prevArchives = await _archiveStore.GetPreviousArchivesAsync(_autoArchiveParsed.Value.volumeNumber).ConfigureAwait(false);
                        if (!_prevArchives.Any(a => a.VolumeNumber == _prevVol))
                        {
                            TM.App.Log($"[WriterPlugin] 检测到新卷第1章，自动存档第{_prevVol}卷...");
                            var _reconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                            await _reconciler.AutoArchiveVolumeIfNeededAsync(_prevVol).ConfigureAwait(false);
                            TM.App.Log($"[WriterPlugin] 第{_prevVol}卷自动存档完成");
                        }
                    }
                    catch (Exception _archiveEx)
                    {
                        TM.App.Log($"[WriterPlugin] 第{_prevVol}卷自动存档失败（不阻断生成）: {_archiveEx.Message}");
                    }
                }

                var contentServiceF2 = ServiceLocator.Get<GeneratedContentService>();
                if (contentServiceF2.ChapterExists(chapterId))
                {
                    var dupMsg = $"章节 {chapterId} 已存在。如需重新生成请使用 @重写:{chapterId} 指令。";
                    TM.App.Log($"[WriterPlugin] 重复生成拦截: {dupMsg}");
                    throw new InvalidOperationException(dupMsg);
                }

                await EnsureSequentialChapterContinuityAsync(ct, chapterId).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                GenerationProgressHub.ReportPhase(ProgressPhase.Preparing, $"正在准备生成 {chapterId}：加载打包上下文...");

                var specTask = ServiceLocator.Get<SpecLoader>().LoadProjectSpecAsync();

                var guideService = ServiceLocator.Get<GuideContextService>();
                var ctx = await guideService.BuildContentContextAsync(chapterId, ct).ConfigureAwait(false);
                if (ctx == null)
                {
                    var errorMsg = $"无法获取章节 {chapterId} 的打包上下文，请确认已执行打包";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot == null)
                {
                    var errorMsg = $"章节 {chapterId} 为正式版上下文（打包+MD），但 FactSnapshot 缺失。请重新打包或修复账本后重试。";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                GenerationProgressHub.Report("正在并行加载前置数据...");
                using var vectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var vectorTask = PopulateLongDistanceRecallAsync(ctx, vectorCts.Token);

                if (ctx.ContextMode == ContentContextMode.Full && ctx.FactSnapshot != null)
                {
                    ct.ThrowIfCancellationRequested();
                    var changeDetection = ServiceLocator.Get<IChangeDetectionService>();
                    GenerationProgressHub.Report("正在校验打包一致性：扫描模块变更...");
                    await changeDetection.RefreshAllAsync().ConfigureAwait(false);
                    var configService = ServiceLocator.Get<ContentConfigService>();

                    var enabledChangedModules = changeDetection.GetChangedModules()
                        .Where(m => configService.IsModuleEnabled(m))
                        .ToList();

                    if (enabledChangedModules.Count > 0)
                    {
                        var errorMsg = "检测到已启用模块存在未打包变更，正式版生成条件未满足。请先重新打包后重试。\n" +
                                       string.Join("\n", enabledChangedModules.Select(m =>
                                           "- " + m.Replace("Generate", "生成")
                                                   .Replace("Design", "设计")
                                                   .Replace("GlobalSettings", "全局设置")
                                                   .Replace("Elements", "元素")));
                        TM.App.Log($"[WriterPlugin] {errorMsg}");
                        vectorCts.Cancel();
                        throw new InvalidOperationException(errorMsg);
                    }

                    var contextIdsValidation = await guideService.ValidateContextIdsAsync(ctx.ContextIds).ConfigureAwait(false);
                    if (!contextIdsValidation.IsValid)
                    {
                        var errorMsg = $"ContextIds 解析失败，索引与本体不一致。\n{contextIdsValidation.GetErrorSummary()}";
                        TM.App.Log($"[WriterPlugin] {errorMsg}");
                        vectorCts.Cancel();
                        throw new InvalidOperationException(errorMsg);
                    }
                }

                ct.ThrowIfCancellationRequested();

                var projectSpec = await specTask.ConfigureAwait(false);
                var effectiveSpec = CreativeSpec.Merge(projectSpec, null);
                await vectorTask.ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                string rawContent;

                if (ctx.FactSnapshot == null && ctx.ContextMode != ContentContextMode.Full)
                {
                    var msg = $"章节 {chapterId} 缺少 FactSnapshot，强一致模式下禁止轻量生成/跳过一致性校验。请先完成打包后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    throw new InvalidOperationException(msg);
                }

                TM.App.Log($"[WriterPlugin] gen: {chapterId}");
                GenerationProgressHub.Report("准备完成，开始正式生成章节...");
                var genResult = await ServiceLocator.Get<AutoRewriteEngine>().GenerateWithRewriteAsync(
                    chapterId,
                    ctx,
                    ctx.FactSnapshot!,
                    effectiveSpec,
                    ct).ConfigureAwait(false);

                if (!genResult.Success)
                {
                    if (genResult.RequiresManualIntervention)
                    {
                        TM.App.Log($"[WriterPlugin] 需要人工介入: {chapterId}");
                        throw new TM.Services.Framework.AI.SemanticKernel.ManualInterventionRequiredException($"{genResult.InterventionHint}\n最后失败原因：{string.Join("; ", genResult.GetLastFailureReasons())}");
                    }

                    var errorMsg = string.IsNullOrWhiteSpace(genResult.ErrorMessage)
                        ? "AI 未返回任何内容"
                        : genResult.ErrorMessage;
                    TM.App.Log($"[WriterPlugin] AI生成失败: {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                rawContent = genResult.Content!;
                TM.App.Log($"[WriterPlugin] gen ok: {chapterId}, attempts: {genResult.TotalAttempts}");

                ct.ThrowIfCancellationRequested();

                var cleaned = StripLeadingTitle(
                    StripPromptEchoKeepChanges(CleanContentKeepChanges(rawContent), ctx.Title));

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                var effectiveSnapshot3 = ctx.FactSnapshot ?? await TryBuildLazySnapshotAsync(chapterId, ctx.ContextIds).ConfigureAwait(false);
                if (effectiveSnapshot3 != null)
                {
                    await callback.OnContentGeneratedStrictAsync(
                        chapterId,
                        cleaned,
                        effectiveSnapshot3,
                        genResult.GateResult,
                        genResult.DesignElements,
                        ctx.ContextIds).ConfigureAwait(false);
                }
                else
                {
                    var msg = $"{chapterId} FactSnapshot不可用，强一致模式下禁止降级为非严格保存。请检查打包上下文是否完整后重试。";
                    TM.App.Log($"[WriterPlugin] {msg}");
                    vectorCts.Cancel();
                    throw new InvalidOperationException(msg);
                }

                var cleanedBody = GenerationGate.StripChangesSection(cleaned);
                var actualWordCount = CountWords(cleanedBody);
                var title = string.IsNullOrWhiteSpace(ctx.Title) ? chapterId : ctx.Title;

                TM.App.Log($"[WriterPlugin] 章节生成成功: {chapterId}, 字数: {actualWordCount}");
                GlobalToast.Success("章节已保存", $"「{title}」约 {actualWordCount} 字");

                var persisted = await ServiceLocator.Get<GeneratedContentService>().GetChapterAsync(chapterId).ConfigureAwait(false);
                var displayContent = persisted ?? cleaned;

                CurrentChapterTracker.SetCurrentChapter(chapterId, title);
                await TryAutoSwitchVolumeAfterGenerationAsync(chapterId).ConfigureAwait(false);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Comm.PublishRefreshChapterList();
                    Comm.PublishChapterSelected(chapterId, BuildCanonicalTabTitle(chapterId, title), displayContent);
                    StandardDialog.FlashTaskbarIfBackground(System.Windows.Application.Current.MainWindow);
                });

                return $"已生成章节「{title}」，约 {actualWordCount} 字。";
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 生成已取消");
                throw;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WriterPlugin] 异常: {ex.Message}");
                throw;
            }
        }

        [KernelFunction("GenerateChapterByNumber")]
        [Description("按章节号生成章节内容；卷归属由当前启用卷自动推断，免去构造完整章节ID。")]
        public async Task<string> GenerateChapterByNumberAsync(
            CancellationToken ct,
            [Description("章节号（仅数字），如 3 表示第3章")] int chapterNumber)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapterByNumber: 第{chapterNumber}章");

            try
            {
                ct.ThrowIfCancellationRequested();

                var volumeNumber = await ResolveVolumeNumberForChapterAsync(ct, chapterNumber).ConfigureAwait(false);
                var chapterId = ChapterParserHelper.BuildChapterId(volumeNumber, chapterNumber);

                GlobalToast.Info("卷归属", $"第{chapterNumber}章 → 第{volumeNumber}卷");

                return await GenerateChapterAsync(ct, chapterId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        [KernelFunction("GenerateChapterFromSource")]
        [Description("基于来源章节续写下一章（@续写 入口）：自动计算目标章节ID并复用正常生成流程。")]
        public async Task<string> GenerateChapterFromSourceAsync(
            CancellationToken ct,
            [Description("来源章节ID，如 vol1_ch3")] string sourceChapterId)
        {
            TM.App.Log($"[WriterPlugin] GenerateChapterFromSource: {sourceChapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                var contentService = ServiceLocator.Get<GeneratedContentService>();

                if (!contentService.ChapterExists(sourceChapterId))
                {
                    var errorMsg = $"来源章节 {sourceChapterId} 不存在";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                ct.ThrowIfCancellationRequested();

                var targetChapterId = await contentService.GenerateNextChapterIdFromSourceAsync(sourceChapterId).ConfigureAwait(false);
                TM.App.Log($"[WriterPlugin] 续写目标章节: {sourceChapterId} → {targetChapterId}");

                ct.ThrowIfCancellationRequested();

                return await GenerateChapterAsync(ct, targetChapterId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 续写已取消");
                throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        [KernelFunction("RewriteChapter")]
        [Description("硬重写指定已存在章节（@重写 入口）：基于目标章节自身上下文重新生成，旧内容会被备份用于差异对比。")]
        public async Task<string> RewriteChapterAsync(
            CancellationToken ct,
            [Description("目标章节ID，如 vol1_ch3，必须已存在")] string targetChapterId)
        {
            TM.App.Log($"[WriterPlugin] RewriteChapter(硬重写): {targetChapterId}");

            try
            {
                ct.ThrowIfCancellationRequested();

                var contentService = ServiceLocator.Get<GeneratedContentService>();

                if (!contentService.ChapterExists(targetChapterId))
                {
                    var errorMsg = $"目标章节 {targetChapterId} 不存在，请先生成该章节或检查章节ID";
                    TM.App.Log($"[WriterPlugin] {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                ct.ThrowIfCancellationRequested();

                var targetParsed = ChapterParserHelper.ParseChapterId(targetChapterId);
                var allChapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
                var chaptersToDelete = new List<string>();

                if (targetParsed.HasValue)
                {
                    var targetVol = targetParsed.Value.volumeNumber;
                    var targetCh = targetParsed.Value.chapterNumber;

                    chaptersToDelete = allChapters
                        .Where(c => c.VolumeNumber > targetVol
                            || (c.VolumeNumber == targetVol && c.ChapterNumber >= targetCh))
                        .OrderBy(c => c.VolumeNumber)
                        .ThenBy(c => c.ChapterNumber)
                        .Select(c => c.Id)
                        .ToList();
                }

                if (chaptersToDelete.Count == 0)
                    chaptersToDelete.Add(targetChapterId);

                if (chaptersToDelete.Count > 1)
                {
                    var subsequentCount = chaptersToDelete.Count - 1;
                    GlobalToast.Warning("重写级联删除",
                        $"重写 {targetChapterId} 将同时删除后续 {subsequentCount} 个章节以保证剧情连贯性");
                    TM.App.Log($"[WriterPlugin] 硬重写：将级联删除 {chaptersToDelete.Count} 个章节（{chaptersToDelete.First()} ~ {chaptersToDelete.Last()}）");
                }

                for (var i = chaptersToDelete.Count - 1; i >= 0; i--)
                {
                    var chId = chaptersToDelete[i];
                    ct.ThrowIfCancellationRequested();
                    TM.App.Log($"[WriterPlugin] 硬重写：级联删除 {chId}");
                    var deleted = await contentService.DeleteChapterAsync(chId).ConfigureAwait(false);
                    if (!deleted && contentService.ChapterExists(chId))
                    {
                        TM.App.Log($"[WriterPlugin] 硬重写：删除 {chId} 失败（文件可能被占用）");
                        var logMsg = $"以下章节文件无法删除（可能被占用），已中止重写：{chId}";
                        var userMsg = $"以下章节文件无法删除/移动（IO错误），已中止重写：{chId}";
                        TM.App.Log($"[WriterPlugin] {logMsg}");
                        throw new InvalidOperationException(userMsg);
                    }
                }
                TM.App.Log($"[WriterPlugin] 硬重写：级联删除完成，共删除 {chaptersToDelete.Count} 个章节");

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Comm.PublishRefreshChapterList();
                });

                ct.ThrowIfCancellationRequested();

                var result = await GenerateChapterAsync(ct, targetChapterId).ConfigureAwait(false);

                return result;
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[WriterPlugin] 重写已取消");
                throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

    }
}
