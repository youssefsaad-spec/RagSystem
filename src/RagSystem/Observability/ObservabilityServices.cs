using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RagSystem.Observability.Settings;

namespace RagSystem.Observability;

public static class ObservabilityServices
{
    /// <summary>
    /// Registers the LangSmith settings and, when a key is set, an OpenTelemetry tracer that sends the spans of
    /// <see cref="Telemetry.Source"/> to LangSmith over OTLP/HTTP (protobuf). Without a key nothing is registered and the app runs
    /// exactly as before. Only our own spans are sent: no ASP.NET Core, HttpClient or EF Core spans, which would bury the RAG
    /// steps in LangSmith. A wrong setting stops the app at start.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = (configuration.GetSection(LangSmithSettings.SectionName).Get<LangSmithSettings>() ?? new LangSmithSettings()).EnsureValid();
        services.AddSingleton(settings);
        Telemetry.CaptureContent = settings.CaptureContent;

        if (!settings.IsActive)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(settings.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.SourceName)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(settings.TracesUrl);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf; // LangSmith takes OTLP over HTTP, not gRPC
                    options.Headers = settings.Headers;
                }));
        return services;
    }
}
