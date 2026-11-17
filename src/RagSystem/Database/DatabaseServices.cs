using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RagSystem.Database;

public static class DatabaseServices
{
    /// <summary>
    /// Registers the database context and the <c>database</c> health check. Nothing connects at start: a missing
    /// connection string only shows when the database is first used (the health check then reports it).
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        // Read when first needed, so configuration added after this point (by tests) is seen too.
        services.AddSingleton(provider =>
            provider.GetRequiredService<IConfiguration>().GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()
            ?? new DatabaseSettings());

        // One data source for the whole app; it knows the pgvector type.
        services.AddSingleton(provider =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Db");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The connection string is not set. Set the environment variable ConnectionStrings__Db.");
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });
        services.AddDbContext<RagDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.UseVector()));

        services.AddHealthChecks().AddDbContextCheck<RagDbContext>("database");
        return services;
    }

    /// <summary>Applies pending migrations when <c>Database:MigrateOnStartup</c> is true.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<DatabaseSettings>().MigrateOnStartup)
            return;

        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RagDbContext>().Database.MigrateAsync();
    }
}
