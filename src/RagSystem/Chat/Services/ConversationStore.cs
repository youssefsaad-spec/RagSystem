using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RagSystem.Chat.Dtos;
using RagSystem.Chat.Exceptions;
using RagSystem.Chat.Models;
using RagSystem.Chat.Rules;
using RagSystem.Chat.Settings;
using RagSystem.Database;
using RagSystem.Database.Entities;
using RagSystem.Retrieval.Services;
using RagSystem.Text;

namespace RagSystem.Chat.Services;

/// <summary>
/// Saves and loads conversations (F26). A conversation of another client or version is "not found" (it looks the same as one
/// that does not exist). Until F29 there are no clients, so <c>clientId</c> is null everywhere.
/// </summary>
public sealed class ConversationStore(RagDbContext db, ChatSettings settings, ChunkCitations citations, TimeProvider clock)
{
    /// <summary>The earlier pairs of the conversation for the prompt: up to the turn and token limits, oldest first.</summary>
    /// <exception cref="ConversationNotFoundException">No such conversation for this client and version.</exception>
    public async Task<IReadOnlyList<HistoryTurn>> LoadHistoryAsync(Guid conversationId, string version, Guid? clientId, CancellationToken ct)
    {
        await EnsureExistsAsync(conversationId, version, clientId, ct);

        // Newest first. Two messages per pair, so this reads at most the pairs that can be used.
        var newest = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Role)
            .Take(settings.HistoryMaxTurns * 2)
            .ToListAsync(ct);

        return HistoryTrimmer.Trim(Pairs(newest), settings.HistoryMaxTurns, settings.HistoryMaxTokens);
    }

    /// <summary>
    /// Saves a question and its answer. A new conversation is created now, not when the request started, so a request that
    /// failed earlier leaves nothing behind.
    /// </summary>
    /// <param name="conversationId">The conversation to add to, or null to start one.</param>
    /// <exception cref="ConversationNotFoundException">No such conversation for this client and version.</exception>
    public async Task<(Guid ConversationId, Guid AnswerId)> SaveTurnAsync(
        Guid? conversationId, string version, Guid? clientId, NewTurn turn, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        Conversation conversation;
        if (conversationId is { } id)
        {
            conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.Version == version && c.ClientId == clientId, ct)
                           ?? throw new ConversationNotFoundException(id);
            conversation.LastMessageAt = now;
        }
        else
        {
            conversation = new Conversation { Id = Guid.NewGuid(), ClientId = clientId, Version = version, CreatedAt = now, LastMessageAt = now };
            db.Conversations.Add(conversation);
        }

        var answer = new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = MessageRoles.Assistant, Content = turn.Answer,
            ModelVersion = turn.ModelVersion, Model = turn.Model, EstTokens = TokenEstimator.Estimate(turn.Answer),
            InputTokens = turn.InputTokens, OutputTokens = turn.OutputTokens, ThinkingTokens = turn.ThinkingTokens,
            CitedChunkIds = turn.CitedChunkIds.ToArray(), GuardFlags = FlagsJson(turn.AssistantFlags), CreatedAt = now,
        };
        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = MessageRoles.User, Content = turn.Question,
            RetrievalQuery = turn.RetrievalQuery, EstTokens = TokenEstimator.Estimate(turn.Question),
            GuardFlags = FlagsJson(turn.UserFlags), CreatedAt = now,
        });
        db.Messages.Add(answer);

        await db.SaveChangesAsync(ct);
        return (conversation.Id, answer.Id);
    }

    /// <summary>All messages of the conversation, oldest first, with the citations of the answers.</summary>
    /// <exception cref="ConversationNotFoundException">No such conversation for this client and version.</exception>
    public async Task<IReadOnlyList<MessageView>> GetMessagesAsync(Guid conversationId, string version, Guid? clientId, CancellationToken ct)
    {
        await EnsureExistsAsync(conversationId, version, clientId, ct);

        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ThenByDescending(m => m.Role) // a question and its answer saved together: the question first
            .ToListAsync(ct);

        var found = (await citations.FindAsync(version, messages.SelectMany(m => m.CitedChunkIds).Distinct().ToList(), ct))
            .ToDictionary(c => c.ChunkId);

        return messages.Select(m => new MessageView(
            m.Id,
            m.Role,
            m.Content,
            m.CitedChunkIds.Where(found.ContainsKey).Select(id => ChatCitation.Of(found[id].DocumentTitle, found[id].Ordinal)).ToList(),
            m.Role == MessageRoles.Assistant ? new ChatUsage(m.InputTokens, m.OutputTokens, m.ThinkingTokens, m.CacheHit) : null,
            m.CreatedAt)).ToList();
    }

    private async Task EnsureExistsAsync(Guid conversationId, string version, Guid? clientId, CancellationToken ct)
    {
        var exists = await db.Conversations.AsNoTracking()
            .AnyAsync(c => c.Id == conversationId && c.Version == version && c.ClientId == clientId, ct);
        if (!exists)
            throw new ConversationNotFoundException(conversationId);
    }

    /// <summary>Whole question-and-answer pairs from messages listed newest first (an answer, then its question). A stray message is skipped.</summary>
    private static IEnumerable<HistoryTurn> Pairs(IReadOnlyList<Message> newestFirst)
    {
        for (var i = 0; i < newestFirst.Count; i++)
        {
            if (i + 1 < newestFirst.Count && newestFirst[i].Role == MessageRoles.Assistant && newestFirst[i + 1].Role == MessageRoles.User)
            {
                var (answer, question) = (newestFirst[i], newestFirst[i + 1]);
                yield return new HistoryTurn(question.Content, answer.Content, question.EstTokens + answer.EstTokens);
                i++;
            }
        }
    }

    private static string? FlagsJson(IReadOnlyDictionary<string, string>? flags) =>
        flags is { Count: > 0 } ? JsonSerializer.Serialize(flags) : null;
}
