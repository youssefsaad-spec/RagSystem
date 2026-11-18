using Microsoft.EntityFrameworkCore;
using Pgvector;
using RagSystem.Chat.Exceptions;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Chat.Services;
using RagSystem.Chat.Settings;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Retrieval.Services;
using RagSystem.Tests.Database;
using RagSystem.Tests.Documents;

namespace RagSystem.Tests.Chat.Services;

/// <summary>The conversation tables and the store on a real Postgres (skipped unless RAGSYSTEM_TEST_DB is set).</summary>
public class ConversationStoreTests
{
    private static async Task<(TestDatabase Database, RagDbContext Context)> NewDatabaseAsync()
    {
        var database = await TestDatabase.CreateAsync();
        var context = ApiHost.NewContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return (database, context);
    }

    private static ConversationStore StoreOn(RagDbContext context, ChatSettings? settings = null, TimeProvider? clock = null) =>
        new(context, settings ?? new ChatSettings(), new ChunkCitations(context), clock ?? TimeProvider.System);

    private static NewTurn Turn(string question, string answer, IReadOnlyList<Guid>? cited = null) =>
        new(question, question, null, answer, "gemini-test", "gemini-test-001", 100, 20, 0, cited ?? [], null);

    [RequiresDatabaseFact]
    public async Task The_first_save_creates_the_conversation_and_two_messages_and_a_later_save_adds_to_it()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var store = StoreOn(context);

        var (id, firstAnswer) = await store.SaveTurnAsync(null, "v3", null, Turn("Q1", "A1"), CancellationToken.None);
        var (sameId, secondAnswer) = await store.SaveTurnAsync(id, "v3", null, Turn("Q2", "A2"), CancellationToken.None);

        Assert.Equal(id, sameId);
        Assert.NotEqual(firstAnswer, secondAnswer);
        Assert.Single(await context.Conversations.ToListAsync());
        Assert.Equal(4, await context.Messages.CountAsync());
        var saved = await context.Messages.SingleAsync(m => m.Id == firstAnswer);
        Assert.Equal((MessageRoles.Assistant, "gemini-test", "gemini-test-001", 100, 20), (saved.Role, saved.Model, saved.ModelVersion, saved.InputTokens, saved.OutputTokens));
    }

    [RequiresDatabaseFact]
    public async Task Saving_updates_last_message_at()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var store = StoreOn(context, clock: clock);
        var (id, _) = await store.SaveTurnAsync(null, "v3", null, Turn("Q1", "A1"), CancellationToken.None);

        clock.Now = clock.Now.AddMinutes(5);
        await store.SaveTurnAsync(id, "v3", null, Turn("Q2", "A2"), CancellationToken.None);

        var conversation = await context.Conversations.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), conversation.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero), conversation.LastMessageAt);
    }

    [RequiresDatabaseFact]
    public async Task History_is_whole_pairs_oldest_first_even_when_a_question_and_its_answer_have_the_same_time()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var clock = new ManualClock(DateTimeOffset.UtcNow); // every save at the same instant, like a fast "not covered" turn
        var store = StoreOn(context, clock: clock);
        var (id, _) = await store.SaveTurnAsync(null, "v3", null, Turn("Q1", "A1"), CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(1);
        await store.SaveTurnAsync(id, "v3", null, Turn("Q2", "A2"), CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(1);
        await store.SaveTurnAsync(id, "v3", null, Turn("Q3", "A3"), CancellationToken.None);

        var history = await store.LoadHistoryAsync(id, "v3", null, CancellationToken.None);

        Assert.Equal([("Q1", "A1"), ("Q2", "A2"), ("Q3", "A3")], history.Select(t => (t.Question, t.Answer)));
    }

    [RequiresDatabaseFact]
    public async Task History_stops_at_the_turn_limit_and_at_the_token_limit_with_the_newest_pairs_kept()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var store = StoreOn(context, new ChatSettings { HistoryMaxTurns = 2 }, clock);
        Guid? id = null;
        foreach (var n in Enumerable.Range(1, 4))
        {
            id = (await store.SaveTurnAsync(id, "v3", null, Turn($"Q{n}", $"A{n}"), CancellationToken.None)).ConversationId;
            clock.Now = clock.Now.AddSeconds(1);
        }

        Assert.Equal(["Q3", "Q4"], (await store.LoadHistoryAsync(id!.Value, "v3", null, CancellationToken.None)).Select(t => t.Question));

        // Each pair here is about 2 tokens (a 4-character question and answer); a limit of 3 holds one pair.
        var tight = StoreOn(context, new ChatSettings { HistoryMaxTokens = 3 }, clock);
        Assert.Equal(["Q4"], (await tight.LoadHistoryAsync(id.Value, "v3", null, CancellationToken.None)).Select(t => t.Question));
    }

    [RequiresDatabaseFact]
    public async Task A_conversation_of_another_version_or_another_client_is_not_found_for_load_save_and_list()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var store = StoreOn(context);
        var owner = Guid.NewGuid();
        var (id, _) = await store.SaveTurnAsync(null, "v3", owner, Turn("Q1", "A1"), CancellationToken.None);

        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadHistoryAsync(id, "v2", owner, CancellationToken.None));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadHistoryAsync(id, "v3", Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.LoadHistoryAsync(id, "v3", null, CancellationToken.None));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.SaveTurnAsync(id, "v3", Guid.NewGuid(), Turn("Q2", "A2"), CancellationToken.None));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.GetMessagesAsync(id, "v2", owner, CancellationToken.None));
        await Assert.ThrowsAsync<ConversationNotFoundException>(() => store.GetMessagesAsync(Guid.NewGuid(), "v3", owner, CancellationToken.None));
        Assert.Equal(2, await context.Messages.CountAsync()); // the refused save added nothing
        Assert.NotEmpty(await store.LoadHistoryAsync(id, "v3", owner, CancellationToken.None)); // the owner still gets in
    }

    [RequiresDatabaseFact]
    public async Task Messages_come_with_citations_built_from_the_chunk_ids_and_a_deleted_chunk_drops_out()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var document = new Document { Version = "v3", FileName = "g.md", Title = "NDC Portal APIs V3", Status = DocumentStatus.Ready };
        context.Documents.Add(document);
        var chunks = Enumerable.Range(0, 2).Select(i => new Chunk
        {
            DocumentId = document.Id, Version = "v3", Ordinal = 11 + i, Content = "c", EstTokens = 1,
            Embedding = new Vector(new float[RagDbContext.EmbeddingDimensions]), EmbeddingModel = "test",
        }).ToList();
        context.Chunks.AddRange(chunks);
        await context.SaveChangesAsync();
        var store = StoreOn(context);
        var (id, _) = await store.SaveTurnAsync(null, "v3", null, Turn("Q1", "A1", [chunks[0].Id, chunks[1].Id, Guid.NewGuid()]), CancellationToken.None);

        var messages = await store.GetMessagesAsync(id, "v3", null, CancellationToken.None);

        Assert.Equal([MessageRoles.User, MessageRoles.Assistant], messages.Select(m => m.Role));
        Assert.Equal(["NDC Portal APIs V3, part 12", "NDC Portal APIs V3, part 13"], messages[1].Citations.Select(c => c.Text)); // the unknown id is left out
        Assert.Null(messages[0].Usage);
        Assert.Equal((100, 20, 0, false), (messages[1].Usage!.InputTokens, messages[1].Usage!.OutputTokens, messages[1].Usage!.ThinkingTokens, messages[1].Usage!.CacheHit));
    }

    [RequiresDatabaseFact]
    public async Task Flags_are_saved_as_json_and_a_turn_without_flags_saves_null()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var store = StoreOn(context);
        var turn = Turn("Q1", "A1") with
        {
            UserFlags = new Dictionary<string, string> { ["rewrite"] = "timeout" },
            AssistantFlags = new Dictionary<string, string>(),
        };

        await store.SaveTurnAsync(null, "v3", null, turn, CancellationToken.None);

        var userFlags = (await context.Messages.SingleAsync(m => m.Role == MessageRoles.User)).GuardFlags;
        Assert.Equal(new Dictionary<string, string> { ["rewrite"] = "timeout" }, System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(userFlags!));
        Assert.Null((await context.Messages.SingleAsync(m => m.Role == MessageRoles.Assistant)).GuardFlags);
    }

    [RequiresDatabaseFact]
    public async Task Deleting_a_conversation_deletes_its_messages()
    {
        var (database, context) = await NewDatabaseAsync();
        await using var databaseScope = database;
        await using var contextScope = context;
        var (id, _) = await StoreOn(context).SaveTurnAsync(null, "v3", null, Turn("Q1", "A1"), CancellationToken.None);

        await context.Conversations.Where(c => c.Id == id).ExecuteDeleteAsync();

        Assert.Equal(0, await context.Messages.CountAsync());
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
