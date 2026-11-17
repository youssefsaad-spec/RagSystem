using Microsoft.Extensions.AI;
using RagSystem.Ollama.Settings;

namespace RagSystem.Ollama.Services;

/// <summary>
/// Lets the Ollama embedding client be used through the Microsoft.Extensions.AI interface, the same one Gemini uses, so the
/// rest of the app does not know which provider it is talking to. Failures are <c>EmbeddingException</c>s from the client.
/// </summary>
public sealed class OllamaEmbeddingGenerator(OllamaEmbeddingClient client, OllamaSettings settings)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly EmbeddingGeneratorMetadata _metadata =
        new("ollama", new Uri(settings.BaseUrl), settings.EmbeddingModel, settings.EmbeddingDimensions);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var vectors = await client.EmbedAsync(values.ToList(), cancellationToken);
        return new GeneratedEmbeddings<Embedding<float>>(vectors.Select(v => new Embedding<float>(v)));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
        // The HttpClient belongs to the factory that made it.
    }
}
