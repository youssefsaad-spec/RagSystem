using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RagSystem.Database;

/// <summary>
/// Used only by <c>dotnet ef</c> when it creates a migration. Creating a migration never connects, so the connection
/// string here is a placeholder and holds no secret.
/// </summary>
public sealed class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql("Host=localhost;Database=ragsystem_design", npgsql => npgsql.UseVector())
            .Options;
        return new RagDbContext(options);
    }
}
