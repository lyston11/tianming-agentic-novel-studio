using System.IO;

namespace TM.Services.Modules.ProjectData.Implementations.Indexing
{
    public sealed class ChapterEmbeddingIndex : FileBasedVectorIndex
    {
        public ChapterEmbeddingIndex() : base("ChapterEmbedding") { }

        protected override string GetFilePath()
        {
            return Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides", "chapter_embeddings.json");
        }
    }
}
