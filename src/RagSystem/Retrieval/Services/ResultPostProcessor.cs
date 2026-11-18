using RagSystem.Observability;
using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Settings;
using System;
using System.Numerics.Tensors;

namespace RagSystem.Retrieval.Services;

/// <summary>
/// Turns the candidates of the vector search into the documentation the model reads. Pure code: no database, no model.
/// In this order: drop weak matches, drop near-duplicates, keep what fits the token budget, order by document, label S1, S2...
/// </summary>
public sealed class ResultPostProcessor(RetrievalSettings settings)
{
    /// <summary>Tokens the prompt adds around each source (its opening and closing tags).</summary>
    public const int WrapperTokens = 12;

    /// <param name="candidates">As the search returned them: best match first.</param>
    public RetrievalResult Process(IReadOnlyList<ChunkCandidate> candidates) =>
        Telemetry.Trace("post_process", Telemetry.Kinds.Chain, span =>
        {
            span.SetInputJson(new
            {
                candidates = candidates.Count,
                min_score = settings.MinScore,
                duplicate_similarity = settings.DuplicateSimilarity,
                context_budget_tokens = settings.ContextBudgetTokens,
            });

            var result = ProcessCore(candidates, out var tokensUsed);

            // What each step did: how many chunks it removed and which ones (ids, scores and reasons; no text).
            span.SetOutputJson(new
            {
                kept = result.Sources.Select(s => new { label = s.Label, chunk_id = s.ChunkId, document = s.DocumentTitle, ordinal = s.Ordinal, score = Math.Round(s.Score, 4) }),
                dropped = result.Dropped.Select(d => new
                {
                    reason = d.Reason.ToString(),
                    chunk_id = d.Candidate.ChunkId,
                    document = d.Candidate.DocumentTitle,
                    ordinal = d.Candidate.Ordinal,
                    score = Math.Round(d.Candidate.Score, 4),
                }),
            });
            span.SetMeta("kept", result.Sources.Count);
            span.SetMeta("dropped_below_min_score", result.Dropped.Count(d => d.Reason == DropReason.BelowMinScore));
            span.SetMeta("dropped_near_duplicate", result.Dropped.Count(d => d.Reason == DropReason.NearDuplicate));
            span.SetMeta("dropped_over_budget", result.Dropped.Count(d => d.Reason == DropReason.OverBudget));
            span.SetMeta("tokens_used", tokensUsed);
            span.SetMeta("not_covered", result.IsEmpty); // nothing left: the model will not be called
            return result;
        });

    private RetrievalResult ProcessCore(IReadOnlyList<ChunkCandidate> candidates, out int tokensUsed)
    {
        var dropped = new List<DroppedCandidate>();

        // 1. Weak matches.
        var strong = new List<ChunkCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Score < settings.MinScore)
                dropped.Add(new DroppedCandidate(candidate, DropReason.BelowMinScore));
            else
                strong.Add(candidate);
        }

        //  Near-duplicates of a chunk that was already chosen (the better-ranked one wins).
        // 2.two chunks have a very close similarity so they are considered duplicates. The first one is kept, the second one is dropped.
        var distinct = new List<ChunkCandidate>();
        foreach (var candidate in strong)
        {
            if (distinct.Any(chosen => TensorPrimitives.CosineSimilarity(chosen.Embedding, candidate.Embedding) > settings.DuplicateSimilarity))
                dropped.Add(new DroppedCandidate(candidate, DropReason.NearDuplicate));
            else
                distinct.Add(candidate);
        }


        // 3.if the tokens out of context window budget, drop starting from the lowest similar untile we fit the budget
        var kept = new List<ChunkCandidate>();
        var used = 0;
        foreach (var candidate in distinct)
        {
            var cost = candidate.EstTokens + WrapperTokens;
            if (kept.Count > 0 && used + cost > settings.ContextBudgetTokens)
            {
                dropped.Add(new DroppedCandidate(candidate, DropReason.OverBudget));
                continue;
            }
            kept.Add(candidate);
            used += cost;
        }

        // 4. By document (the document with the best match first), then by position in the document.
        // now we filtered the candidates, we need to order them by document  and then by position in each document
        // for example 
        //Guide, part 40 (best score)
        //What's New, part 2
        //Guide, part 11

        //-----after---------
        //S1 = Guide, part 11
        //S2 = Guide, part 40
        //S3 = What's New, part 2
        var documentRank = new Dictionary<Guid, int>();
        for (var rank = 0; rank < kept.Count; rank++)
            documentRank.TryAdd(kept[rank].DocumentId, rank);

        var sources = kept
            .OrderBy(c => documentRank[c.DocumentId])
            .ThenBy(c => c.Ordinal)
            .Select((c, index) => new RetrievedSource($"S{index + 1}", c.DocumentTitle, c.Ordinal, c.Content, c.EstTokens, c.ChunkId, c.Score))
            .ToList();

        // The dropped list is in rank order, not in the order of the steps.
        tokensUsed = used;
        var rankOf = candidates.Select((c, i) => (c.ChunkId, i)).ToDictionary(x => x.ChunkId, x => x.i);
        return new RetrievalResult(sources, dropped.OrderBy(d => rankOf[d.Candidate.ChunkId]).ToList());
    }
}
