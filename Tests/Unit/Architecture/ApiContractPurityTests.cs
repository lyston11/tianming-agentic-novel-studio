using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Services.AgentSessions;
using TM.Web.NovelAgentWeb.Services.Memory;
using Xunit;
using Microsoft.AspNetCore.Mvc;

namespace Tests.Unit.Architecture;

public class ApiContractPurityTests
{
    [Fact]
    public void ProjectResponse_DoesNotExposeFilesystemStorageName()
    {
        Assert.DoesNotContain(
            typeof(ProjectResponse).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "StorageProjectName", StringComparison.Ordinal));
    }

    [Fact]
    public void NovelProjectEntity_DoesNotKeepFilesystemStorageIdentity()
    {
        Assert.DoesNotContain(
            typeof(NovelProject).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "StorageProjectName", StringComparison.Ordinal));
    }

    [Fact]
    public void DbContext_DoesNotMapFilesystemStorageIdentity()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(NovelProject));

        Assert.NotNull(entityType);
        Assert.Null(entityType!.FindProperty("StorageProjectName"));
        Assert.DoesNotContain(
            entityType.GetIndexes(),
            index => index.Properties.Any(p => string.Equals(p.Name, "StorageProjectName", StringComparison.Ordinal)));
    }

    [Fact]
    public void AgentSessionService_DoesNotExposeRawSessionDataWriter()
    {
        Assert.DoesNotContain(
            typeof(IAgentSessionService).GetMethods().Select(m => m.Name),
            name => string.Equals(name, "SaveSessionStateAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionMemory_DoesNotTrackUploadedKnowledgeAsChatMemory()
    {
        Assert.DoesNotContain(
            typeof(SessionMemory).GetProperties().Select(p => p.Name),
            name => string.Equals(name, "RecentUploadedKnowledgeIds", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateKnowledgeRequest_DoesNotAllowClientProvidedSourceFileIdentity()
    {
        var properties = typeof(CreateKnowledgeRequest)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SourceFileId", properties);
        Assert.DoesNotContain("SourceType", properties);
        Assert.DoesNotContain("ChunkIndex", properties);
        Assert.DoesNotContain("ExtractionContext", properties);
    }

    [Fact]
    public void KnowledgeBaseEntity_UsesUploadTaskIdentityNotSourceFileIdentity()
    {
        var properties = typeof(KnowledgeBase)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SourceFileId", properties);
        Assert.Contains("SourceUploadTaskId", properties);

        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(KnowledgeBase));

        Assert.NotNull(entityType);
        Assert.Null(entityType!.FindProperty("SourceFileId"));

        var uploadTaskProperty = entityType.FindProperty("SourceUploadTaskId");
        Assert.NotNull(uploadTaskProperty);
        Assert.Equal("source_upload_task_id", uploadTaskProperty!.GetColumnName());
    }

    [Fact]
    public void KnowledgeResponse_SeparatesUsageContextFromSourceProject()
    {
        var properties = typeof(KnowledgeResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ProjectId", properties);
        Assert.Contains("UsageProjectId", properties);
        Assert.Contains("SourceProjectId", properties);
    }

    [Fact]
    public void ProjectController_ExposesOnlyCanonicalProjectsRoute()
    {
        var routes = typeof(ProjectController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToList();

        Assert.Contains("api/projects", routes);
        Assert.DoesNotContain("api/project", routes);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }
}
