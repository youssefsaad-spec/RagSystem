using Microsoft.EntityFrameworkCore;
using RagSystem.Database;

namespace RagSystem.Documents.Services;

public static class CorpusRevision
{
    /// <summary>The version's current revision, or 0 when nothing was ever stored for it.</summary>
    public static async Task<int> CurrentAsync(this RagDbContext db, string version, CancellationToken ct) =>
        await db.CorpusStates.AsNoTracking().Where(s => s.Version == version).Select(s => s.Revision).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Adds 1 to the version's revision (or creates it at 1). Call it inside the transaction that changes the version's
    /// content, so the cache of answers (F35) never outlives the content it was built from.
    /// </summary>
    public static Task<int> BumpAsync(this RagDbContext db, string version, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO corpus_state (version, revision) VALUES ({version}, 1)
             ON CONFLICT (version) DO UPDATE SET revision = corpus_state.revision + 1
             """, ct);
}
