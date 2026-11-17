using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagSystem.Tests;

/// <summary>
/// The app as the tests start it. It runs in the "Testing" environment, so appsettings.Development.json (the local
/// database and MigrateOnStartup) is not read and a test never touches a developer's database.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    /// <summary>
    /// A connection string that points nowhere (nothing listens on port 1). Services that need a database can be created,
    /// and a test that really needs a database replaces it with <c>UseSetting("ConnectionStrings:Db", ...)</c>.
    /// </summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Password=none;Timeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Db", UnreachableDatabase);
    }
}
