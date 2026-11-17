using Microsoft.EntityFrameworkCore;
using RagSystem.Database;

namespace RagSystem.Documents.Services;

public sealed class DocumentDeleter(RagDbContext db)
{
    /// <summary>
    /// Deletes the document and, by cascade, its chunks, and bumps the version's revision, in one transaction.
    /// </summary>
    /// <returns>False when there is no such document in that version.</returns>
    public async Task<bool> DeleteAsync(string version, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var deleted = await db.Documents.Where(d => d.Id == id && d.Version == version).ExecuteDeleteAsync(ct);
        if (deleted == 0)
            return false;

        await db.BumpAsync(version, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
