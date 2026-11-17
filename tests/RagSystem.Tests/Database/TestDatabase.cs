using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using RagSystem.Database;

namespace RagSystem.Tests.Database;

public static class TestContexts
{
    /// <summary>
    /// Options for a context on a test database. Every test has its own database, so every context gets its own internal
    /// EF service provider, and EF raises an error after 20 of them in one process. That is a fact of the tests (one app
    /// runs for a whole real process), so the warning is switched off here.
    /// </summary>
    public static DbContextOptions<RagDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(connectionString, n => n.UseVector())
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
}

/// <summary>
/// A throw-away Postgres database for one test. The tests that use it run only when the environment variable
/// <c>RAGSYSTEM_TEST_DB</c> holds a connection string to a Postgres server with pgvector (for example the one from
/// docker-compose.yml); each test creates its own database and drops it at the end.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string EnvironmentVariable = "RAGSYSTEM_TEST_DB";

    private readonly string _serverConnectionString;
    private readonly string _name;

    private TestDatabase(string serverConnectionString, string name, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    /// <summary>The connection string of the new, empty database.</summary>
    public string ConnectionString { get; }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static async Task<TestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(EnvironmentVariable)
                     ?? throw new InvalidOperationException($"{EnvironmentVariable} is not set.");
        var name = "ragsystem_test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        var own = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
        return new TestDatabase(server, name, own);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A test that needs a real Postgres. It is skipped (not failed) when <c>RAGSYSTEM_TEST_DB</c> is not set.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public RequiresDatabaseFactAttribute()
    {
        if (!TestDatabase.IsConfigured)
            Skip = $"Set {TestDatabase.EnvironmentVariable} to a Postgres with pgvector to run this test.";
    }
}

/// <summary>Same as <see cref="RequiresDatabaseFactAttribute"/>, for a test with data rows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresDatabaseTheoryAttribute : TheoryAttribute
{
    public RequiresDatabaseTheoryAttribute()
    {
        if (!TestDatabase.IsConfigured)
            Skip = $"Set {TestDatabase.EnvironmentVariable} to a Postgres with pgvector to run this test.";
    }
}
