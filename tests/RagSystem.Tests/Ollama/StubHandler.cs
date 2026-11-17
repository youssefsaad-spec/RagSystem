using System.Net;
using System.Text;

namespace RagSystem.Tests.Ollama;

/// <summary>A fake Ollama server: answers each request with what the test gave it and records the requests.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
        return await respond(request, cancellationToken);
    }

    public static StubHandler Json(HttpStatusCode status, string json) =>
        new((_, _) => Task.FromResult(Reply(status, json)));

    public static StubHandler Throws(Exception exception) => new((_, _) => throw exception);

    public static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>The embeddings answer for these vectors: <c>{"embeddings":[[...],[...]]}</c>.</summary>
    public static string EmbeddingsJson(IEnumerable<float[]> vectors) =>
        "{\"model\":\"qwen3-embedding:0.6b\",\"embeddings\":[" +
        string.Join(",", vectors.Select(v => "[" + string.Join(",", v.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]")) +
        "]}";

    /// <summary>An answer with one valid vector for every text in the request (what a working Ollama does).</summary>
    public static HttpResponseMessage EmbeddingsForEveryText(HttpRequestMessage request)
    {
        using var body = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        var count = body.RootElement.GetProperty("input").GetArrayLength();
        return Reply(HttpStatusCode.OK, EmbeddingsJson(Enumerable.Range(0, count).Select(i => Vector(i + 1f))));
    }

    /// <summary>A vector of the right size whose first value is <paramref name="first"/>.</summary>
    public static float[] Vector(float first, int length = 1024) => Enumerable.Range(0, length).Select(i => i == 0 ? first : 0f).ToArray();
}
