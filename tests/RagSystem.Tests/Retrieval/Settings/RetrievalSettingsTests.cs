using Microsoft.Extensions.DependencyInjection;
using RagSystem.Retrieval.Settings;

namespace RagSystem.Tests.Retrieval.Settings;

public class RetrievalSettingsTests
{
    [Fact]
    public void The_defaults_are_the_agreed_values_and_valid()
    {
        var settings = new RetrievalSettings().EnsureValid();

        Assert.Equal(20, settings.TopK);
        Assert.Equal(0.60, settings.MinScore);
        Assert.Equal(0.95, settings.DuplicateSimilarity);
        Assert.Equal(5000, settings.ContextBudgetTokens);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(RetrievalSettings.MaxTopK)]
    public void A_top_k_from_1_to_the_maximum_is_accepted(int topK)
    {
        Assert.Equal(topK, new RetrievalSettings { TopK = topK }.EnsureValid().TopK);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RetrievalSettings.MaxTopK + 1)]
    public void A_top_k_outside_the_range_is_refused(int topK)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new RetrievalSettings { TopK = topK }.EnsureValid());

        Assert.Contains("Retrieval:TopK", error.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void A_score_outside_0_to_1_is_refused(double value)
    {
        Assert.Throws<InvalidOperationException>(() => new RetrievalSettings { MinScore = value }.EnsureValid());
        Assert.Throws<InvalidOperationException>(() => new RetrievalSettings { DuplicateSimilarity = value }.EnsureValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_edges_of_the_score_range_are_accepted(double value)
    {
        new RetrievalSettings { MinScore = value, DuplicateSimilarity = value }.EnsureValid();
    }

    [Fact]
    public void A_budget_of_0_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => new RetrievalSettings { ContextBudgetTokens = 0 }.EnsureValid());
    }

    [Fact]
    public void The_values_are_read_from_the_Retrieval_section()
    {
        using var factory = new TestApp().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Retrieval:TopK", "7");
            builder.UseSetting("Retrieval:MinScore", "0.5");
        });

        var settings = factory.Services.GetRequiredService<RetrievalSettings>();

        Assert.Equal(7, settings.TopK);
        Assert.Equal(0.5, settings.MinScore);
        Assert.Equal(0.95, settings.DuplicateSimilarity); // not set: the default
    }

    [Fact]
    public void The_app_refuses_to_start_with_a_wrong_value()
    {
        using var factory = new TestApp().WithWebHostBuilder(builder => builder.UseSetting("Retrieval:TopK", "0"));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Retrieval:TopK", error.ToString());
    }
}
