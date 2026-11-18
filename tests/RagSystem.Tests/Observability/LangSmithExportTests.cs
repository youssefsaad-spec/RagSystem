using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using RagSystem.Observability;

namespace RagSystem.Tests.Observability;

/// <summary>
/// Real OpenTelemetry export to a local server that plays LangSmith: proves the spans are posted to /otel/v1/traces as protobuf with
/// the headers LangSmith needs. No network and no real key.
/// </summary>
[Collection("Telemetry")]
public class LangSmithExportTests
{
    private sealed record Received(string Path, string ContentType, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    private static async Task<(WebApplication App, ConcurrentQueue<Received> Requests, string BaseUrl)> StartFakeLangSmithAsync()
    {
        var requests = new ConcurrentQueue<Received>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/{**path}", async (HttpContext http) =>
        {
            using var body = new MemoryStream();
            await http.Request.Body.CopyToAsync(body);
            requests.Enqueue(new Received(
                http.Request.Path, http.Request.ContentType ?? "",
                http.Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString()), body.ToArray()));
            return Results.Ok();
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, requests, address.TrimEnd('/'));
    }

    private static ServiceProvider TracingTo(string endpoint, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["LangSmith:Endpoint"] = endpoint + "/otel",
            ["LangSmith:ApiKey"] = "test-key",
            ["LangSmith:Project"] = "test-project",
        };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddObservability(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Spans_are_posted_to_otel_v1_traces_as_protobuf_with_the_key_and_the_project()
    {
        var (fake, requests, baseUrl) = await StartFakeLangSmithAsync();
        await using var _ = fake;
        await using var provider = TracingTo(baseUrl);
        var tracer = provider.GetRequiredService<TracerProvider>();

        // An ASP.NET Core request activity that nobody records, as in a real request: the trace must still be sent.
        using var unrecordedParent = new Activity("http-request").Start();
        using (var span = Telemetry.StartRoot("chat_turn", Telemetry.Kinds.Chain))
        {
            Assert.NotNull(span);
            span.SetInput("how do I book a flight?");
            span.SetMeta("version", "v3");
        }

        Assert.True(tracer.ForceFlush(10_000));

        var request = Assert.Single(requests);
        Assert.Equal("/otel/v1/traces", request.Path);
        Assert.Equal("application/x-protobuf", request.ContentType);
        Assert.Equal("test-key", request.Headers["x-api-key"]);
        Assert.Equal("test-project", request.Headers["langsmith-project"]);
        var body = Encoding.UTF8.GetString(request.Body); // protobuf keeps strings as plain UTF-8 bytes
        Assert.Contains("chat_turn", body);
        Assert.Contains("langsmith.span.kind", body);
        Assert.Contains("how do I book a flight?", body);
        Assert.Contains("langsmith.metadata.version", body);
        Assert.Contains("ragsystem", body); // the service name
    }

    [Fact]
    public async Task Without_a_key_nothing_is_registered_and_nothing_is_sent()
    {
        var (fake, requests, baseUrl) = await StartFakeLangSmithAsync();
        await using var _ = fake;
        await using var provider = TracingTo(baseUrl, new() { ["LangSmith:ApiKey"] = "" });

        using (var span = Telemetry.StartRoot("chat_turn", Telemetry.Kinds.Chain))
            Assert.Null(span); // no listener: the span is not even created

        Assert.Null(provider.GetService<TracerProvider>());
        Assert.Empty(requests);
    }
}
