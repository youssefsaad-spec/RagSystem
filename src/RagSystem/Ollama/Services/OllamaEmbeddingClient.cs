using RagSystem.Embeddings.Exceptions;
using RagSystem.Ollama.Settings;

namespace RagSystem.Ollama.Services;

/// <summary>
/// Asks Ollama for embeddings with <c>POST /api/embed</c>. One call embeds a list of texts. The vectors are returned as
/// Ollama sent them (the batching, retrying and normalising is F13); only the count and the length are checked here.
/// </summary>
public sealed class OllamaEmbeddingClient(HttpClient http, OllamaSettings settings)
{
    /// <summary>One vector per text, in the same order.</summary>
    /// <exception cref="EmbeddingException">The call failed (see <see cref="EmbeddingException.Kind"/>) or the answer is unusable.</exception>
    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return [];

        var request = new EmbedRequest(
            settings.EmbeddingModel,
            texts,
            settings.EmbeddingDimensions,
            settings.EmbeddingOnCpu ? new EmbedOptions(NumGpu: 0) : null);
        var reply = await OllamaHttp.PostAsync<EmbedReply>(http, "/api/embed", request, ct);

        var vectors = reply.Embeddings ?? [];
        if (vectors.Count != texts.Count)
            throw new EmbeddingException(EmbeddingFailureKind.Fatal, $"Ollama returned {vectors.Count} embeddings for {texts.Count} texts.");

        var wrong = vectors.FirstOrDefault(v => v.Length != settings.EmbeddingDimensions);
        if (wrong is not null)
            throw new EmbeddingException(EmbeddingFailureKind.Fatal,
                $"Expected {settings.EmbeddingDimensions} dimensions but Ollama returned {wrong.Length}.");

        return [.. vectors];
    }

    private sealed record EmbedRequest(string Model, IReadOnlyList<string> Input, int Dimensions, EmbedOptions? Options);

    private sealed record EmbedOptions(int NumGpu);

    private sealed record EmbedReply(List<float[]>? Embeddings);
}
