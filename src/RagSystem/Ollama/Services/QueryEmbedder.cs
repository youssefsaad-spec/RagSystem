using RagSystem.Ollama.Exceptions;

namespace RagSystem.Ollama.Services;

/// <summary>
/// Embeds one question for the search. The question is sent exactly as the user wrote it, the same way the chunks were
/// embedded, so question and chunks are compared in the same way. A single call, no retry: a failure goes to the caller,
/// who answers 503 or 502 (F24).
/// </summary>
public sealed class QueryEmbedder(OllamaEmbeddingClient client)
{
    /// <summary>The vector of the question, length 1, with the length checked.</summary>
    /// <exception cref="OllamaException">The call failed (see <see cref="OllamaException.Kind"/>) or the answer is unusable.</exception>
    public async Task<float[]> EmbedAsync(string question, CancellationToken ct = default)
    {
        var vectors = await client.EmbedAsync([question], ct);
        return UnitVector.Of(vectors[0]);
    }
}
