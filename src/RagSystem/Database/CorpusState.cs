namespace RagSystem.Database;

/// <summary>
/// One row per version. <see cref="Revision"/> goes up whenever that version's content changes (a document is stored
/// or deleted). The semantic cache (F35) uses it to drop answers that were given for older content.
/// </summary>
public sealed class CorpusState
{
    public string Version { get; set; } = "";

    public int Revision { get; set; }
}
