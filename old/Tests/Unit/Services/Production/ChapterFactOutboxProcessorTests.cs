using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterFactOutboxProcessorTests
{
    [Fact]
    public async Task ProcessAsync_RestoresPayloadAndRunsLlmFactWriterWithPersister()
    {
        var writer = new RecordingChapterFactWriter();
        var completion = new RecordingWritingModelCompletionService();
        var persister = new RecordingChapterContinuityFactPersister();
        var snapshotUpdater = new RecordingChapterFactSnapshotUpdater();
        var processor = new ChapterFactOutboxProcessor(
            writer,
            completion,
            persister,
            snapshotUpdater,
            NullLogger<ChapterFactOutboxProcessor>.Instance);
        var evt = new OutboxEvent
        {
            Id = "outbox-1",
            UserId = "user-1",
            ProjectId = "project-1",
            RuntimeRunId = "run-1",
            EventType = "extract_chapter_continuity_facts",
            AggregateType = "chapter",
            AggregateId = "chapter-001",
            PayloadJson = JsonSerializer.Serialize(new
            {
                run = new NovelAgentRun
                {
                    RunId = "run-1",
                    TargetChapterId = "chapter-001"
                },
                contextPackage = new ChapterContextPackageSummary
                {
                    ChapterId = "chapter-001",
                    PackageId = "pkg-1",
                    HardContinuityFacts = { "银蓝邮徽不能攻击。" }
                },
                committedContent = "沈砚按住银蓝邮徽，邮徽只帮他辨认旧邮路。"
            })
        };

        await processor.ProcessAsync(evt);

        Assert.NotNull(writer.Request);
        Assert.Equal("chapter-001", writer.Request!.Run.TargetChapterId);
        Assert.Equal("pkg-1", writer.Request.ContextPackage.PackageId);
        Assert.Contains("辨认旧邮路", writer.Request.CommittedContent);
        Assert.Equal("user-1", completion.UserId);
        Assert.Equal("user-1", persister.UserId);
        Assert.Equal("project-1", persister.ProjectId);
        Assert.Equal("沈砚", persister.Facts?.ProtagonistName);
        Assert.Equal("user-1", snapshotUpdater.UserId);
        Assert.Equal("project-1", snapshotUpdater.ProjectId);
        Assert.Equal("chapter-001", snapshotUpdater.ChapterId);
        Assert.Equal("run-1", snapshotUpdater.RuntimeRunId);
        Assert.Equal("pkg-1", snapshotUpdater.PackageId);
        Assert.Equal("沈砚", snapshotUpdater.Facts?.ProtagonistName);
    }

    private sealed class RecordingChapterFactWriter : IChapterFactWriter
    {
        public ChapterFactWriteRequest? Request { get; private set; }

        public async Task<ChapterFactWriteResult> ExtractAndPersistAsync(
            ChapterFactWriteRequest request,
            CancellationToken ct = default)
        {
            Request = request;
            Assert.NotNull(request.CompleteAsync);
            Assert.NotNull(request.PersistAsync);

            await request.CompleteAsync!("system", "user", ct);
            var facts = new ChapterContinuityFacts
            {
                ChapterId = request.Run.TargetChapterId,
                ProtagonistName = "沈砚"
            };
            var commit = await request.PersistAsync!(facts, ct);
            return new ChapterFactWriteResult
            {
                Success = commit.Success,
                Message = commit.Message,
                Facts = facts,
                CommitResult = commit
            };
        }
    }

    private sealed class RecordingWritingModelCompletionService : IWritingModelCompletionService
    {
        public string UserId { get; private set; } = string.Empty;

        public Task<string> CompleteAsync(
            string userId,
            string system,
            string user,
            CancellationToken ct = default)
        {
            UserId = userId;
            return Task.FromResult("{}");
        }
    }

    private sealed class RecordingChapterContinuityFactPersister : IChapterContinuityFactPersister
    {
        public string UserId { get; private set; } = string.Empty;
        public string ProjectId { get; private set; } = string.Empty;
        public ChapterContinuityFacts? Facts { get; private set; }

        public Task<StoryBibleCommitResult> PersistAsync(
            string userId,
            string projectId,
            ChapterContinuityFacts facts,
            CancellationToken ct = default)
        {
            UserId = userId;
            ProjectId = projectId;
            Facts = facts;
            return Task.FromResult(new StoryBibleCommitResult
            {
                Success = true,
                Message = "ok"
            });
        }
    }

    private sealed class RecordingChapterFactSnapshotUpdater : IChapterFactSnapshotUpdater
    {
        public string UserId { get; private set; } = string.Empty;
        public string ProjectId { get; private set; } = string.Empty;
        public string ChapterId { get; private set; } = string.Empty;
        public string RuntimeRunId { get; private set; } = string.Empty;
        public string PackageId { get; private set; } = string.Empty;
        public ChapterContinuityFacts? Facts { get; private set; }

        public Task UpdateAsync(
            string userId,
            string projectId,
            string chapterId,
            string runtimeRunId,
            string? packageId,
            ChapterContinuityFacts facts,
            CancellationToken ct = default)
        {
            UserId = userId;
            ProjectId = projectId;
            ChapterId = chapterId;
            RuntimeRunId = runtimeRunId;
            PackageId = packageId ?? string.Empty;
            Facts = facts;
            return Task.CompletedTask;
        }
    }
}
