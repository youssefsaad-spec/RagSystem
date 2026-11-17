using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RagSystem.Ollama.Exceptions;

namespace RagSystem.Ollama.Services;

/// <summary>Posts JSON to Ollama and turns every failure into an <see cref="OllamaException"/> with a kind.</summary>
internal static class OllamaHttp
{
    /// <summary>Ollama's fields are snake_case: <c>num_gpu</c>, <c>keep_alive</c>.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <exception cref="OperationCanceledException">Only when the caller cancelled. A timeout is a transient <see cref="OllamaException"/>.</exception>
    public static async Task<T> PostAsync<T>(HttpClient http, string path, object body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, body, Json, ct);
            if (!response.IsSuccessStatusCode)
                throw await FailureFrom(response, ct);

            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new OllamaException(OllamaFailureKind.Fatal, $"Ollama {path} returned an empty answer.");
        }
        catch (OllamaException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The HttpClient timeout, not the caller.
            throw new OllamaException(OllamaFailureKind.Transient, $"Ollama {path} timed out.");
        }
        catch (HttpRequestException ex)
        {
            // Connection refused or reset: Ollama is starting or restarting.
            throw new OllamaException(OllamaFailureKind.Transient, $"Ollama could not be reached: {ex.Message}", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new OllamaException(OllamaFailureKind.Fatal, $"Ollama {path} returned an answer that is not valid JSON.", inner: ex);
        }
    }

    private static async Task<OllamaException> FailureFrom(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var message = $"Ollama answered {status}: {await ErrorText(response, ct)}";

        if (status == 429)
            return new OllamaException(OllamaFailureKind.RateLimited, message, RetryAfter(response));
        if (status >= 500)
            return new OllamaException(OllamaFailureKind.Transient, message);
        return new OllamaException(OllamaFailureKind.Fatal, message); // 404 model not found, 400 bad request, ...
    }

    /// <summary>Ollama explains an error as <c>{"error":"model 'x' not found"}</c>.</summary>
    private static async Task<string> ErrorText(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? "";
        }
        catch (JsonException)
        {
            // Not JSON (for example a proxy page): use the plain text.
        }
        return text.Length <= 200 ? text : text[..200] + "...";
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - DateTimeOffset.UtcNow is { } wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        return null;
    }
}
