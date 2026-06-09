namespace TM.Web.NovelAgentWeb.Services.Vectorization;

/// <summary>
/// Provides text chunking functionality for vectorization of large materials.
/// Splits long text into smaller chunks with overlap for improved semantic retrieval.
/// </summary>
public interface IMaterialChunker
{
    /// <summary>
    /// Splits text into overlapping chunks suitable for embedding and vector storage.
    /// </summary>
    /// <param name="text">The text content to chunk</param>
    /// <param name="materialId">The unique identifier of the material being chunked</param>
    /// <returns>A list of MaterialChunk objects with metadata</returns>
    List<MaterialChunk> ChunkText(string text, string materialId);
}
