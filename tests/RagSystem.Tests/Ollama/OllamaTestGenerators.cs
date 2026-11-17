using Microsoft.Extensions.AI;
using RagSystem.Embeddings.Services;
using RagSystem.Ollama.Services;
using RagSystem.Ollama.Settings;

namespace RagSystem.Tests.Ollama;

public static class OllamaTestGenerators
{
    /// <summary>The embedding generator the app builds for Ollama (client, adapter and guard), talking to a fake Ollama.</summary>
    public static IEmbeddingGenerator<string, Embedding<float>> For(StubHandler handler)
    {
        var settings = new OllamaSettings();
        var client = new OllamaEmbeddingClient(new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") }, settings);
        return new GuardedEmbeddingGenerator("Ollama", settings.EmbeddingDimensions, () => new OllamaEmbeddingGenerator(client, settings));
    }
}
