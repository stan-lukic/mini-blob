using Microsoft.EntityFrameworkCore;
using MiniBlob.Api.Data;
using MiniBlob.Api.Data.Models;
using MiniBlob.Api.Models;

namespace MiniBlob.Api.Services;

public class EfSearchIndex : ISearchIndex
{
    private readonly BlobDbContext _db;
    public EfSearchIndex(BlobDbContext db) { _db = db; }

    public async Task AddOrUpdateAsync(IndexRecord rec)
    {
        var existing = await _db.Blobs.FirstOrDefaultAsync(b => b.Container == rec.Container && b.BlobPath == rec.BlobPath);
        var json = System.Text.Json.JsonSerializer.Serialize(new { displayName = rec.Filename, createdUtc = rec.CreatedUtc, createdBy = rec.CreatedBy });
        if (existing == null)
        {
            await _db.Blobs.AddAsync(new BlobMetadata {
                Container = rec.Container,
                BlobPath = rec.BlobPath,
                FileName = rec.Filename,
                CreatedBy = rec.CreatedBy,
                CreatedUtc = rec.CreatedUtc,
                Size = rec.Size,
                ETag = Guid.NewGuid().ToString(),
                MetadataJson = json
            });
        }
        else
        {
            existing.Size = rec.Size;
            existing.ETag = Guid.NewGuid().ToString();
            existing.MetadataJson = json;
            _db.Update(existing);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<SearchResult> SearchAsync(string query, int page, int pageSize)
    {
        var q = query ?? string.Empty;
        var itemsQuery = _db.Blobs.Where(b => b.FileName.Contains(q) || b.BlobPath.Contains(q) || b.CreatedBy.Contains(q))
            .OrderByDescending(b => b.CreatedUtc);
        var total = await itemsQuery.CountAsync();
        var items = await itemsQuery.Skip((page-1)*pageSize).Take(pageSize).ToListAsync();
        var records = items.Select(i => new IndexRecord(i.Container, i.BlobPath, i.FileName, i.CreatedUtc, i.CreatedBy, i.Size));
        return new SearchResult(total,1,50, records);
    }
}
