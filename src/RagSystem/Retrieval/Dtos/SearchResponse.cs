using RagSystem.Retrieval.Models;
using RagSystem.Retrieval.Rules;

namespace RagSystem.Retrieval.Dtos;

/// <summary>
/// What a search shows: the chunks that would be given to the model (in the order it reads them) and every candidate that
/// was left out with the reason. A tool for checking and tuning retrieval; it calls no chat model.
/// </summary>
/// <param name="Candidates">How many chunks the vector search found before post-processing (at most <c>Retrieval:TopK</c>).</param>
public sealed record SearchResponse(
    string Version,
    string Question,
    int Candidates,
    IReadOnlyList<SearchSource> Sources,
    IReadOnlyList<SearchDropped> Dropped)
{
    public static SearchResponse From(string version, string question, RetrievalResult result) => new(
        version,
        question,
        result.Sources.Count + result.Dropped.Count,
        result.Sources.Select(s => new SearchSource(s.Label, CitationText.Of(s.DocumentTitle, s.Ordinal), s.Score, s.EstTokens, s.ChunkId, s.Content)).ToList(),
        result.Dropped.Select(d => new SearchDropped(CitationText.Of(d.Candidate.DocumentTitle, d.Candidate.Ordinal), d.Candidate.Score, d.Reason.ToString(), d.Candidate.ChunkId)).ToList());
}

/// <param name="Label">S1, S2... as the model would cite it.</param>
/// <param name="Citation">For example "NDC Portal APIs V3, part 12".</param>
public sealed record SearchSource(string Label, string Citation, double Score, int EstTokens, Guid ChunkId, string Content);

/// <param name="Reason">BelowMinScore, NearDuplicate or OverBudget.</param>
public sealed record SearchDropped(string Citation, double Score, string Reason, Guid ChunkId);
