using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace RagSystem.Database;

/// <summary>The database of RagSystem. Tables and columns are snake_case. More tables are added by later features.</summary>
public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Size of an embedding. It is fixed by the column type: another size needs a migration and a full re-embed.
    /// The Ollama client (F12) checks at start that its setting is the same.
    /// </summary>
    public const int EmbeddingDimensions = 1024;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    public DbSet<CorpusState> CorpusStates => Set<CorpusState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector: the "vector" column type used for embeddings.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(document =>
        {
            document.ToTable("documents");
            document.Property(d => d.Status).HasConversion<string>();
            document.Property(d => d.UploadedAt).HasDefaultValueSql("now()");
            // The same file name in the same version is the same document.
            document.HasIndex(d => new { d.Version, d.FileName }).IsUnique().HasDatabaseName("ix_documents_version_file_name");
        });

        modelBuilder.Entity<Chunk>(chunk =>
        {
            chunk.ToTable("chunks");
            chunk.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            chunk.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
            chunk.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
            chunk.HasIndex(c => c.Version).HasDatabaseName("ix_chunks_version");
            // No vector index on purpose: a few hundred chunks per version are searched exactly, which is fast and misses nothing.
        });

        modelBuilder.Entity<CorpusState>(state =>
        {
            state.ToTable("corpus_state");
            state.HasKey(s => s.Version);
        });

        // Column names are snake_case: UploadedAt becomes uploaded_at.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name) =>
        Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();
}
