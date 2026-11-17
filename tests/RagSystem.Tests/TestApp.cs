using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagSystem.Tests;

/// <summary>
/// The app as the tests start it. It runs in the "Testing" environment, so appsettings.Development.json (the local
/// database and MigrateOnStartup) is not read and a test never touches a developer's database.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
