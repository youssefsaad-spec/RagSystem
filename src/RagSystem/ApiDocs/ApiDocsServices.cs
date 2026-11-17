using Microsoft.OpenApi;

namespace RagSystem.ApiDocs;

public static class ApiDocsServices
{
    public const string DocumentName = "v1";

    public const string JsonPath = "/openapi/v1.json";

    public const string UiPath = "swagger";

    /// <summary>Registers the generator that describes our controllers. It does nothing until <see cref="UseApiDocs"/> serves it.</summary>
    public static IServiceCollection AddApiDocs(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
            options.SwaggerDoc(DocumentName, new OpenApiInfo { Title = "RagSystem", Version = DocumentName }));
        return services;
    }

    /// <summary>
    /// Serves the description (<c>/openapi/v1.json</c>) and the Swagger page (<c>/swagger</c>) when <c>OpenApi:Enabled</c> is true.
    /// Both are open to anyone who can reach the app, so keep it off anywhere but a local run.
    /// </summary>
    public static WebApplication UseApiDocs(this WebApplication app)
    {
        var settings = app.Configuration.GetSection(ApiDocsSettings.SectionName).Get<ApiDocsSettings>() ?? new ApiDocsSettings();
        if (!settings.Enabled)
            return app;

        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(JsonPath, "RagSystem " + DocumentName);
            options.RoutePrefix = UiPath;
        });
        return app;
    }
}
