using TM.Web.NovelAgentWeb.Services.VectorStore;
using Xunit;

namespace Tests.Unit.Services.VectorStore;

public sealed class QdrantVectorStoreTests
{
    [Fact]
    public void BuildScopedFilterValues_AlwaysAddsUserBoundaryBeforeCallerFilters()
    {
        var filters = QdrantVectorStore.BuildScopedFilterValues(
            "user-1",
            new Dictionary<string, object>
            {
                ["project_id"] = "project-1",
                ["source_type"] = "chapter",
                ["source_id"] = "chapter-001"
            });

        Assert.Collection(filters,
            item =>
            {
                Assert.Equal("user_id", item.Key);
                Assert.Equal("user-1", item.Value);
            },
            item =>
            {
                Assert.Equal("project_id", item.Key);
                Assert.Equal("project-1", item.Value);
            },
            item =>
            {
                Assert.Equal("source_type", item.Key);
                Assert.Equal("chapter", item.Value);
            },
            item =>
            {
                Assert.Equal("source_id", item.Key);
                Assert.Equal("chapter-001", item.Value);
            });
    }

    [Fact]
    public void BuildScopedFilterValues_RejectsCallerUserIdOverride()
    {
        var filters = QdrantVectorStore.BuildScopedFilterValues(
            "user-1",
            new Dictionary<string, object>
            {
                ["user_id"] = "user-2",
                ["project_id"] = "project-1"
            });

        Assert.Single(filters, item => item.Key == "user_id");
        Assert.Equal("user-1", filters.First(item => item.Key == "user_id").Value);
    }
}
