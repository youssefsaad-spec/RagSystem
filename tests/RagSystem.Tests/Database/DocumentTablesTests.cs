using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RagSystem.Database;

namespace RagSystem.Tests.Database;

public class DocumentTablesTests
{
    private static RagDbContext ContextFor(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RagDbContext>().UseNpgsql(connectionString, n => n.UseVector()).Options;
        return new RagDbContext(options);
    }

    /// <summary>A context that never connects: enough to read the model.</summary>
    private static IModel Model()
    {
        using var context = new RagDbContextFactory().CreateDbContext([]);
        return context.Model;
    }

    private static Vector Embedding(float first) =>
        new(Enumerable.Range(0, RagDbContext.EmbeddingDimensions).Select(i => i == 0 ? first : 0f).ToArray());

    private static Document NewDocument(string version = "v3", string fileName = "guide.md") =>
        new() { Version = version, FileName = fileName, Title = "NDC Portal APIs V3" };

    private static Chunk NewChunk(Document document, int ordinal, float first = 1f) => new()
    {
        DocumentId = document.Id,
        Version = document.Version,
        Ordinal = ordinal,
        Content = $"chunk {ordinal}",
        EstTokens = 3,
        Embedding = Embedding(first),
        EmbeddingModel = "qwen3-embedding:0.6b",
    };

    // ---- Model checks: no database needed ----

    [Fact]
    public void The_tables_and_columns_have_the_agreed_snake_case_names()
    {
        var model = Model();

        string[] Columns(Type type) => model.FindEntityType(type)!.GetProperties().Select(p => p.GetColumnName()).Order().ToArray();

        Assert.Equal("documents", model.FindEntityType(typeof(Document))!.GetTableName());
        Assert.Equal("chunks", model.FindEntityType(typeof(Chunk))!.GetTableName());
        Assert.Equal("corpus_state", model.FindEntityType(typeof(CorpusState))!.GetTableName());
        Assert.Equal(
            ["chunk_count", "error", "file_name", "id", "ready_at", "status", "title", "uploaded_at", "version"],
            Columns(typeof(Document)));
        Assert.Equal(
            ["content", "created_at", "document_id", "embedding", "embedding_model", "est_tokens", "id", "ordinal", "version"],
            Columns(typeof(Chunk)));
        Assert.Equal(["revision", "version"], Columns(typeof(CorpusState)));
    }

    [Fact]
    public void The_embedding_column_is_vector_1024_and_the_dimension_constant_agrees()
    {
        var column = Model().FindEntityType(typeof(Chunk))!.FindProperty(nameof(Chunk.Embedding))!;

        Assert.Equal(1024, RagDbContext.EmbeddingDimensions);
        Assert.Equal("vector(1024)", column.GetColumnType());
    }

    [Fact]
    public void Version_and_file_name_are_unique_together_and_chunks_are_indexed_by_version_only()
    {
        var model = Model();

        var documentIndex = Assert.Single(model.FindEntityType(typeof(Document))!.GetIndexes());
        Assert.True(documentIndex.IsUnique);
        Assert.Equal(["Version", "FileName"], documentIndex.Properties.Select(p => p.Name));

        // One index on version, and the one EF makes for the foreign key. No vector index (exact scan).
        var chunkIndexes = model.FindEntityType(typeof(Chunk))!.GetIndexes().Select(i => string.Join(",", i.Properties.Select(p => p.Name))).Order();
        Assert.Equal(["DocumentId", "Version"], chunkIndexes);
        Assert.DoesNotContain(model.FindEntityType(typeof(Chunk))!.GetIndexes(), i => i.GetMethod() is not null);
    }

    [Fact]
    public void Deleting_a_document_cascades_to_its_chunks()
    {
        var key = Model().FindEntityType(typeof(Chunk))!.GetForeignKeys().Single();

        Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior);
        Assert.Equal(typeof(Document), key.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void The_corpus_state_key_is_the_version()
    {
        var key = Model().FindEntityType(typeof(CorpusState))!.FindPrimaryKey()!;

        Assert.Equal(["Version"], key.Properties.Select(p => p.Name));
    }

    // ---- Tests on a real database ----

    [RequiresDatabaseFact]
    public async Task A_document_with_a_chunk_and_its_vector_is_stored_and_read_back()
    {
        await using var database = await TestDatabase.CreateAsync();
        var document = NewDocument();
        await using (var write = ContextFor(database.ConnectionString))
        {
            await write.Database.MigrateAsync();
            write.Documents.Add(document);
            write.Chunks.Add(NewChunk(document, 0, first: 0.5f));
            await write.SaveChangesAsync();
        }

        await using var read = ContextFor(database.ConnectionString);
        var storedDocument = await read.Documents.SingleAsync();
        var storedChunk = await read.Chunks.SingleAsync();

        Assert.Equal(("v3", "guide.md", "NDC Portal APIs V3"), (storedDocument.Version, storedDocument.FileName, storedDocument.Title));
        Assert.Equal(DocumentStatus.Processing, storedDocument.Status);
        Assert.Equal(0, storedDocument.ChunkCount);
        Assert.Null(storedDocument.Error);
        Assert.Null(storedDocument.ReadyAt);
        Assert.True(storedDocument.UploadedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(document.Id, storedChunk.DocumentId);
        Assert.Equal(RagDbContext.EmbeddingDimensions, storedChunk.Embedding.ToArray().Length);
        Assert.Equal(0.5f, storedChunk.Embedding.ToArray()[0]);
        Assert.Equal("qwen3-embedding:0.6b", storedChunk.EmbeddingModel);
    }

    [RequiresDatabaseFact]
    public async Task The_status_is_stored_as_readable_text()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        var document = NewDocument();
        document.Status = DocumentStatus.Failed;
        document.Error = "boom";
        context.Documents.Add(document);
        await context.SaveChangesAsync();

        var status = await context.Database.SqlQueryRaw<string>("SELECT status AS \"Value\" FROM documents").SingleAsync();

        Assert.Equal("Failed", status);
    }

    [RequiresDatabaseFact]
    public async Task The_same_file_name_in_the_same_version_cannot_be_stored_twice_but_another_version_can()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        context.Documents.Add(NewDocument("v3", "guide.md"));
        context.Documents.Add(NewDocument("v2", "guide.md"));
        await context.SaveChangesAsync();

        context.Documents.Add(NewDocument("v3", "guide.md"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [RequiresDatabaseFact]
    public async Task Deleting_a_document_deletes_its_chunks_and_only_its_chunks()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        var first = NewDocument(fileName: "a.md");
        var second = NewDocument(fileName: "b.md");
        context.Documents.AddRange(first, second);
        context.Chunks.AddRange(NewChunk(first, 0), NewChunk(first, 1), NewChunk(second, 0));
        await context.SaveChangesAsync();

        await context.Documents.Where(d => d.Id == first.Id).ExecuteDeleteAsync();

        var remaining = await context.Chunks.ToListAsync();
        Assert.Equal(second.Id, Assert.Single(remaining).DocumentId);
    }

    [RequiresDatabaseFact]
    public async Task A_chunk_of_a_document_that_does_not_exist_is_refused()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();

        context.Chunks.Add(NewChunk(new Document { Id = Guid.NewGuid(), Version = "v3" }, 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [RequiresDatabaseFact]
    public async Task A_vector_of_the_wrong_length_is_refused_by_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        var document = NewDocument();
        context.Documents.Add(document);
        var chunk = NewChunk(document, 0);
        chunk.Embedding = new Vector(new float[10]);
        context.Chunks.Add(chunk);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [RequiresDatabaseFact]
    public async Task The_corpus_state_holds_one_revision_per_version()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        context.CorpusStates.Add(new CorpusState { Version = "v3", Revision = 1 });
        await context.SaveChangesAsync();

        // A second context knows nothing of the first row, so the refusal comes from the database's primary key.
        await using var other = ContextFor(database.ConnectionString);
        other.CorpusStates.Add(new CorpusState { Version = "v3", Revision = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [RequiresDatabaseFact]
    public async Task A_nearest_vector_search_on_one_version_works_with_cosine_distance()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = ContextFor(database.ConnectionString);
        await context.Database.MigrateAsync();
        var v3 = NewDocument("v3", "a.md");
        var v2 = NewDocument("v2", "a.md");
        context.Documents.AddRange(v3, v2);
        context.Chunks.AddRange(NewChunk(v3, 0, first: 1f), NewChunk(v3, 1, first: 1f), NewChunk(v2, 0, first: 1f));
        var other = NewChunk(v3, 2);
        other.Embedding = new Vector(Enumerable.Range(0, RagDbContext.EmbeddingDimensions).Select(i => i == 1 ? 1f : 0f).ToArray());
        context.Chunks.Add(other);
        await context.SaveChangesAsync();

        var query = Embedding(1f);
        var found = await context.Chunks
            .Where(c => c.Version == "v3")
            .OrderBy(c => c.Embedding.CosineDistance(query))
            .Select(c => c.Ordinal)
            .ToListAsync();

        Assert.Equal(3, found.Count);
        Assert.Equal(2, found[^1]);
    }
}
