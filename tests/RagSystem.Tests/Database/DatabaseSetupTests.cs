using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RagSystem.Database;

namespace RagSystem.Tests.Database;

public class DatabaseSetupTests(TestApp factory) : IClassFixture<TestApp>
{
    /// <summary>Nothing listens on port 1, so the connection is refused at once.</summary>
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Password=none;Timeout=2";

    private HttpClient ClientWith(string? connectionString, bool migrateOnStartup = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (connectionString is not null)
                builder.UseSetting("ConnectionStrings:Db", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", migrateOnStartup.ToString());
        }).CreateClient();

    [Fact]
    public async Task Health_is_503_when_the_database_cannot_be_reached()
    {
        var response = await ClientWith(UnreachableDatabase).GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_is_never_200_when_no_connection_string_is_set()
    {
        // A missing connection string is a setup mistake: the error is raised while the context is created. A real server
        // answers 500; the test server hands the exception to the test. Either way the answer is not "Healthy".
        HttpResponseMessage? response = null;
        var error = await Record.ExceptionAsync(async () => response = await ClientWith(connectionString: null).GetAsync("/health"));

        Assert.True(error is not null || response is { IsSuccessStatusCode: false });
        if (error is not null)
            Assert.Contains("ConnectionStrings__Db", error.ToString());
    }

    [Fact]
    public void Starting_with_MigrateOnStartup_and_no_database_stops_the_app_with_a_clear_message()
    {
        var error = Assert.ThrowsAny<Exception>(() => ClientWith(connectionString: null, migrateOnStartup: true));

        Assert.Contains("ConnectionStrings__Db", error.ToString());
    }

    [Fact]
    public void The_app_starts_without_a_database_when_MigrateOnStartup_is_off()
    {
        using var client = ClientWith(connectionString: null);

        Assert.NotNull(client);
    }

    [RequiresDatabaseFact]
    public async Task Health_is_200_when_the_database_answers()
    {
        await using var database = await TestDatabase.CreateAsync();

        var response = await ClientWith(database.ConnectionString).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [RequiresDatabaseFact]
    public async Task MigrateOnStartup_applies_the_migrations_and_the_vector_extension_exists()
    {
        await using var database = await TestDatabase.CreateAsync();

        using var client = ClientWith(database.ConnectionString, migrateOnStartup: true);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname = 'vector'", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [RequiresDatabaseFact]
    public async Task Migrating_twice_is_safe_and_leaves_no_pending_migration()
    {
        await using var database = await TestDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<RagDbContext>().UseNpgsql(database.ConnectionString, n => n.UseVector()).Options;

        await using var context = new RagDbContext(options);
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
