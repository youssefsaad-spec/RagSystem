using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Services;
using RagSystem.Retrieval.Settings;

namespace RagSystem.Tests.Retrieval.Services;

public class ResultPostProcessorTests
{
    private static readonly Guid GuideId = Guid.NewGuid();
    private static readonly Guid WhatsNewId = Guid.NewGuid();

    private static ResultPostProcessor Processor(RetrievalSettings? settings = null) => new(settings ?? new RetrievalSettings());

    /// <summary>A unit vector pointing along axis <paramref name="axis"/>: two different axes have cosine similarity 0.</summary>
    private static float[] Axis(int axis)
    {
        var vector = new float[8];
        vector[axis] = 1f;
        return vector;
    }

    private static ChunkCandidate Candidate(double score, int axis, Guid? document = null, int ordinal = 0, int tokens = 100, string? title = null) =>
        new(Guid.NewGuid(), document ?? GuideId, title ?? "Guide", ordinal, $"text of chunk {ordinal} (axis {axis})", tokens, Axis(axis), score);

    // ---- Step 1: minimum score ----

    [Fact]
    public void A_candidate_under_the_minimum_score_is_dropped_and_one_at_the_minimum_is_kept()
    {
        var weak = Candidate(0.59, axis: 0);
        var exact = Candidate(0.60, axis: 1);

        var result = Processor().Process([exact, weak]);

        Assert.Equal([exact.ChunkId], result.Sources.Select(s => s.ChunkId));
        var dropped = Assert.Single(result.Dropped);
        Assert.Equal((weak, DropReason.BelowMinScore), (dropped.Candidate, dropped.Reason));
    }

    [Fact]
    public void The_minimum_score_comes_from_the_settings()
    {
        var candidate = Candidate(0.55, axis: 0);

        Assert.True(Processor().Process([candidate]).IsEmpty);
        Assert.False(Processor(new RetrievalSettings { MinScore = 0.5 }).Process([candidate]).IsEmpty);
    }

    [Fact]
    public void Nothing_found_or_nothing_relevant_gives_an_empty_result()
    {
        Assert.True(Processor().Process([]).IsEmpty);

        var result = Processor().Process([Candidate(0.2, 0), Candidate(0.1, 1)]);

        Assert.True(result.IsEmpty);
        Assert.Equal(2, result.Dropped.Count);
    }

    // ---- Step 2: near-duplicates ----

    [Fact]
    public void A_near_duplicate_of_a_better_ranked_chunk_is_dropped_and_the_better_one_wins()
    {
        var better = Candidate(0.90, axis: 0, document: GuideId);
        var copy = Candidate(0.85, axis: 0, document: WhatsNewId, title: "What's New");
        var other = Candidate(0.80, axis: 1);

        var result = Processor().Process([better, copy, other]);

        Assert.Equal([better.ChunkId, other.ChunkId], result.Sources.Select(s => s.ChunkId));
        var dropped = Assert.Single(result.Dropped);
        Assert.Equal((copy, DropReason.NearDuplicate), (dropped.Candidate, dropped.Reason));
    }

    [Fact]
    public void The_duplicate_threshold_comes_from_the_settings()
    {
        // Two unit vectors at cosine 0.5: (1, 0) and (0.5, sqrt(0.75)). Above a threshold of 0.4 they are duplicates, under 0.6 they are not.
        var first = Candidate(0.9, axis: 0) with { Embedding = [1f, 0f] };
        var second = Candidate(0.8, axis: 0) with { Embedding = [0.5f, MathF.Sqrt(0.75f)] };

        Assert.Equal(2, Processor(new RetrievalSettings { DuplicateSimilarity = 0.6 }).Process([first, second]).Sources.Count);
        Assert.Single(Processor(new RetrievalSettings { DuplicateSimilarity = 0.4 }).Process([first, second]).Sources);
    }

    [Fact]
    public void A_weak_candidate_is_never_a_reason_to_drop_a_duplicate()
    {
        // The weak one is dropped by the minimum score first, so the later copy has nothing to be a duplicate of.
        var weak = Candidate(0.50, axis: 0);
        var strongCopy = Candidate(0.65, axis: 0);

        var result = Processor().Process([weak, strongCopy]);

        Assert.Equal([strongCopy.ChunkId], result.Sources.Select(s => s.ChunkId));
        Assert.Equal(DropReason.BelowMinScore, Assert.Single(result.Dropped).Reason);
    }

    // ---- Step 3: budget ----

    [Fact]
    public void Chunks_are_kept_in_rank_order_until_the_budget_is_full_and_each_costs_its_tokens_plus_12()
    {
        var settings = new RetrievalSettings { ContextBudgetTokens = 300 };
        var a = Candidate(0.9, axis: 0, tokens: 100); // 112
        var b = Candidate(0.8, axis: 1, tokens: 100); // 224
        var c = Candidate(0.7, axis: 2, tokens: 100); // 336: does not fit

        var result = Processor(settings).Process([a, b, c]);

        Assert.Equal([a.ChunkId, b.ChunkId], result.Sources.Select(s => s.ChunkId));
        var dropped = Assert.Single(result.Dropped);
        Assert.Equal((c, DropReason.OverBudget), (dropped.Candidate, dropped.Reason));
    }

    [Fact]
    public void A_chunk_that_does_not_fit_is_skipped_and_a_smaller_one_after_it_can_still_fit()
    {
        var settings = new RetrievalSettings { ContextBudgetTokens = 300 };
        var a = Candidate(0.9, axis: 0, tokens: 100); // 112
        var big = Candidate(0.8, axis: 1, tokens: 400);
        var small = Candidate(0.7, axis: 2, tokens: 50); // 62 -> 174

        var result = Processor(settings).Process([a, big, small]);

        Assert.Equal([a.ChunkId, small.ChunkId], result.Sources.Select(s => s.ChunkId));
        Assert.Equal(big, Assert.Single(result.Dropped).Candidate);
    }

    [Fact]
    public void The_first_chunk_is_kept_even_when_it_alone_is_over_the_budget()
    {
        var huge = Candidate(0.9, axis: 0, tokens: 9000);

        var result = Processor().Process([huge]);

        Assert.Equal([huge.ChunkId], result.Sources.Select(s => s.ChunkId));
    }

    [Fact]
    public void The_budget_comes_from_the_settings()
    {
        var candidates = new[] { Candidate(0.9, 0, tokens: 100), Candidate(0.8, 1, tokens: 100) };

        Assert.Single(Processor(new RetrievalSettings { ContextBudgetTokens = 150 }).Process(candidates).Sources);
        Assert.Equal(2, Processor(new RetrievalSettings { ContextBudgetTokens = 224 }).Process(candidates).Sources.Count);
    }

    // ---- Step 4: order and labels ----

    [Fact]
    public void Sources_are_grouped_by_document_best_match_first_then_by_position_and_labelled_S1_onwards()
    {
        // Rank: guide part 7, what's new part 2, guide part 3, what's new part 1.
        var guide7 = Candidate(0.95, 0, GuideId, ordinal: 7);
        var news2 = Candidate(0.90, 1, WhatsNewId, ordinal: 2, title: "What's New");
        var guide3 = Candidate(0.85, 2, GuideId, ordinal: 3);
        var news1 = Candidate(0.80, 3, WhatsNewId, ordinal: 1, title: "What's New");

        var result = Processor().Process([guide7, news2, guide3, news1]);

        Assert.Equal(["S1", "S2", "S3", "S4"], result.Sources.Select(s => s.Label));
        Assert.Equal([guide3.ChunkId, guide7.ChunkId, news1.ChunkId, news2.ChunkId], result.Sources.Select(s => s.ChunkId));
    }

    [Fact]
    public void A_source_carries_what_the_prompt_and_the_citation_need()
    {
        var candidate = Candidate(0.77, axis: 0, ordinal: 12, tokens: 321, title: "NDC Portal APIs V3");

        var source = Assert.Single(Processor().Process([candidate]).Sources);

        Assert.Equal(new RetrievedSource("S1", "NDC Portal APIs V3", 12, candidate.Content, 321, candidate.ChunkId, 0.77), source);
    }

    [Fact]
    public void Dropped_candidates_are_listed_in_rank_order_whatever_step_dropped_them()
    {
        var kept = Candidate(0.95, 0, tokens: 100);
        var copy = Candidate(0.90, 0);                  // near-duplicate
        var overBudget = Candidate(0.85, 1, tokens: 6000);
        var weak = Candidate(0.30, 2);                  // below minimum

        var result = Processor().Process([kept, copy, overBudget, weak]);

        Assert.Equal(
            [(copy.ChunkId, DropReason.NearDuplicate), (overBudget.ChunkId, DropReason.OverBudget), (weak.ChunkId, DropReason.BelowMinScore)],
            result.Dropped.Select(d => (d.Candidate.ChunkId, d.Reason)));
    }

    [Fact]
    public void The_same_input_gives_the_same_result()
    {
        var candidates = new[] { Candidate(0.9, 0, GuideId, 4), Candidate(0.8, 1, WhatsNewId, 1), Candidate(0.7, 2, GuideId, 2) };

        var first = Processor().Process(candidates);
        var second = Processor().Process(candidates);

        Assert.Equal(first.Sources, second.Sources);
    }
}
