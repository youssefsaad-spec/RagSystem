using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Documents.Services;
using RagSystem.Retrieval.Services;
using RagSystem.Retrieval.Settings;
using RagSystem.Tests.Database;

namespace RagSystem.Tests.Retrieval.Services;

/// <summary>Tests on a real Postgres with pgvector; skipped unless RAGSYSTEM_TEST_DB is set.</summary>
public class ChunkSearcherTests
{
    private static async Task<RagDbContext> OpenAsync(TestDatabase database)
    {
        var context = new RagDbContext(TestContexts.Options(database.ConnectionString));
        await context.Database.MigrateAsync();
        return context;
    }

    /// <summary>A vector made of two numbers (the rest 0): its direction decides its cosine to a question.</summary>
    private static float[] Direction(float x, float y)
    {
        var vector = new float[RagDbContext.EmbeddingDimensions];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    private static Document AddDocument(RagDbContext db, string version, string title, string fileName = "guide.md")
    {
        var document = new Document { Version = version, FileName = fileName, Title = title, Status = DocumentStatus.Ready };
        db.Documents.Add(document);
        return document;
    }

    private static Chunk AddChunk(RagDbContext db, Document document, int ordinal, float[] embedding)
    {
        var chunk = new Chunk
        {
            DocumentId = document.Id, Version = document.Version, Ordinal = ordinal, Content = $"{document.Title} part {ordinal}",
            EstTokens = 10, Embedding = new Vector(embedding), EmbeddingModel = "qwen3-embedding:0.6b",
        };
        db.Chunks.Add(chunk);
        return chunk;
    }

    [RequiresDatabaseFact]
    public async Task Chunks_come_back_best_match_first_with_a_cosine_score_title_and_embedding()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);
        var document = AddDocument(db, "v3", "NDC Portal APIs V3");
        var exact = AddChunk(db, document, 0, Direction(1, 0));        // cosine 1
        var diagonal = AddChunk(db, document, 1, Direction(1, 1));     // cosine 0.7071
        var opposite = AddChunk(db, document, 2, Direction(0, 1));     // cosine 0
        await db.SaveChangesAsync();

        var found = await new ChunkSearcher(db, new RetrievalSettings()).SearchAsync("v3", Direction(1, 0), default);

        Assert.Equal([exact.Id, diagonal.Id, opposite.Id], found.Select(c => c.ChunkId));
        Assert.Equal(1.0, found[0].Score, precision: 5);
        Assert.Equal(Math.Sqrt(0.5), found[1].Score, precision: 4);
        Assert.Equal(0.0, found[2].Score, precision: 5);
        Assert.All(found, c => Assert.Equal(("NDC Portal APIs V3", document.Id), (c.DocumentTitle, c.DocumentId)));
        Assert.Equal([0, 1, 2], found.Select(c => c.Ordinal));
        Assert.Equal("NDC Portal APIs V3 part 1", found[1].Content);
        Assert.Equal(10, found[1].EstTokens);
        Assert.Equal(RagDbContext.EmbeddingDimensions, found[1].Embedding.Length);
        Assert.Equal(1f, found[1].Embedding[1]);
    }

    [RequiresDatabaseFact]
    public async Task Only_the_top_k_best_chunks_are_returned_and_top_k_comes_from_the_settings()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);
        var document = AddDocument(db, "v3", "Guide");
        for (var i = 0; i < 10; i++)
            AddChunk(db, document, i, Direction(1, i)); // the bigger i, the further from (1, 0)
        await db.SaveChangesAsync();

        var three = await new ChunkSearcher(db, new RetrievalSettings { TopK = 3 }).SearchAsync("v3", Direction(1, 0), default);
        var all = await new ChunkSearcher(db, new RetrievalSettings { TopK = 20 }).SearchAsync("v3", Direction(1, 0), default);

        Assert.Equal([0, 1, 2], three.Select(c => c.Ordinal));
        Assert.Equal(10, all.Count);
    }

    [RequiresDatabaseFact]
    public async Task The_version_is_a_hard_filter()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);
        var v2 = AddDocument(db, "v2", "V2 guide");
        var v3 = AddDocument(db, "v3", "V3 guide");
        AddChunk(db, v2, 0, Direction(1, 0)); // a perfect match, but in the other version
        var inV3 = AddChunk(db, v3, 0, Direction(0, 1));
        await db.SaveChangesAsync();

        var found = await new ChunkSearcher(db, new RetrievalSettings()).SearchAsync("v3", Direction(1, 0), default);

        Assert.Equal(inV3.Id, Assert.Single(found).ChunkId);
    }

    [RequiresDatabaseFact]
    public async Task Chunks_of_several_documents_of_the_version_are_searched_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);
        var guide = AddDocument(db, "v3", "Guide", "guide.md");
        var news = AddDocument(db, "v3", "What's New", "news.md");
        AddChunk(db, guide, 0, Direction(1, 1));
        AddChunk(db, news, 0, Direction(1, 0));
        await db.SaveChangesAsync();

        var found = await new ChunkSearcher(db, new RetrievalSettings()).SearchAsync("v3", Direction(1, 0), default);

        Assert.Equal(["What's New", "Guide"], found.Select(c => c.DocumentTitle));
    }

    [RequiresDatabaseFact]
    public async Task A_version_with_no_chunks_gives_no_candidates()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);

        var found = await new ChunkSearcher(db, new RetrievalSettings()).SearchAsync("v3", Direction(1, 0), default);

        Assert.Empty(found);
    }

    // ---- The other lookups the pipeline needs ----

    [RequiresDatabaseFact]
    public async Task The_corpus_revision_is_0_until_something_is_stored_and_then_follows_the_bumps()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);

        Assert.Equal(0, await db.CurrentAsync("v3", default));

        await db.BumpAsync("v3", default);
        await db.BumpAsync("v3", default);
        await db.BumpAsync("v2", default);

        Assert.Equal(2, await db.CurrentAsync("v3", default));
        Assert.Equal(1, await db.CurrentAsync("v2", default));
    }

    [RequiresDatabaseFact]
    public async Task Citations_are_found_by_chunk_id_in_the_order_asked_and_unknown_or_foreign_ids_are_left_out()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await OpenAsync(database);
        var v3 = AddDocument(db, "v3", "NDC Portal APIs V3");
        var v2 = AddDocument(db, "v2", "NDC Portal APIs V2");
        var first = AddChunk(db, v3, 4, Direction(1, 0));
        var second = AddChunk(db, v3, 9, Direction(0, 1));
        var otherVersion = AddChunk(db, v2, 1, Direction(1, 1));
        await db.SaveChangesAsync();

        var found = await new ChunkCitations(db).FindAsync("v3", [second.Id, Guid.NewGuid(), otherVersion.Id, first.Id, second.Id], default);

        Assert.Equal([(second.Id, "NDC Portal APIs V3", 9), (first.Id, "NDC Portal APIs V3", 4)],
            found.Select(c => (c.ChunkId, c.DocumentTitle, c.Ordinal)));
        Assert.Empty(await new ChunkCitations(db).FindAsync("v3", [], default));
    }
}
