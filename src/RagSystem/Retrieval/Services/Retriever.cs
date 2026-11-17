using RagSystem.Retrieval.Models;
using RagSystem.Ollama.Services;

namespace RagSystem.Retrieval.Services;

/// <summary>
/// The whole retrieval for one question: embed it (F17), search the version (F18), post-process the candidates (F19).
/// The search endpoint uses it now; the chat endpoint (F25) will use the same steps.
/// </summary>
public sealed class Retriever(QueryEmbedder embedder, ChunkSearcher searcher, ResultPostProcessor postProcessor)
{
    /// <exception cref="RagSystem.Ollama.Exceptions.OllamaException">The question could not be embedded.</exception>
    public async Task<RetrievalResult> RetrieveAsync(string version, string question, CancellationToken ct)
    {
        var vector = await embedder.EmbedAsync(question, ct);
        var candidates = await searcher.SearchAsync(version, vector, ct);
        return postProcessor.Process(candidates);
    }
}
