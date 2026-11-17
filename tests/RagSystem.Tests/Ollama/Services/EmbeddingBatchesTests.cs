using RagSystem.Ollama.Services;

namespace RagSystem.Tests.Ollama.Services;

public class EmbeddingBatchesTests
{
    /// <summary>A text of about this many estimated tokens (3.5 characters each).</summary>
    private static string TextOf(int tokens) => new('x', (int)(tokens * 3.5));

    private static int[] Sizes(IReadOnlyList<string> texts) => EmbeddingBatches.Plan(texts).Select(b => b.Count).ToArray();

    [Fact]
    public void The_limits_are_32_texts_and_6000_estimated_tokens()
    {
        Assert.Equal(32, EmbeddingBatches.MaxItems);
        Assert.Equal(6000, EmbeddingBatches.MaxEstimatedTokens);
    }

    [Fact]
    public void No_texts_make_no_batches()
    {
        Assert.Empty(EmbeddingBatches.Plan([]));
    }

    [Fact]
    public void A_few_short_texts_are_one_batch()
    {
        Assert.Equal([3], Sizes(["a", "b", "c"]));
    }

    [Fact]
    public void A_batch_is_closed_at_32_texts()
    {
        var texts = Enumerable.Range(0, 70).Select(i => $"text {i}").ToList();

        Assert.Equal([32, 32, 6], Sizes(texts));
    }

    [Fact]
    public void Exactly_32_texts_are_one_batch()
    {
        Assert.Equal([32], Sizes(Enumerable.Repeat("a", 32).ToList()));
    }

    [Fact]
    public void A_batch_is_closed_before_the_text_that_would_pass_6000_tokens()
    {
        // 3 x 2000 = 6000 fits; the fourth would make 8000.
        var texts = Enumerable.Repeat(TextOf(2000), 7).ToList();

        Assert.Equal([3, 3, 1], Sizes(texts));
    }

    [Fact]
    public void A_single_text_over_the_limit_gets_a_batch_of_its_own_and_its_neighbours_are_not_swallowed()
    {
        var texts = new List<string> { "small", "small", TextOf(9000), "small", "small" };

        Assert.Equal([2, 1, 2], Sizes(texts));
    }

    [Fact]
    public void An_oversize_text_at_the_start_or_the_end_is_alone_too()
    {
        Assert.Equal([1, 1], Sizes([TextOf(9000), "small"]));
        Assert.Equal([1, 1], Sizes(["small", TextOf(9000)]));
    }

    [Fact]
    public void The_batches_cover_every_text_once_in_order()
    {
        var texts = Enumerable.Range(0, 200).Select(i => TextOf(i % 7 * 300 + 1)).ToList();

        var batches = EmbeddingBatches.Plan(texts);

        var next = 0;
        foreach (var (start, count) in batches)
        {
            Assert.Equal(next, start);
            Assert.InRange(count, 1, EmbeddingBatches.MaxItems);
            next = start + count;
        }
        Assert.Equal(texts.Count, next);
    }
}
