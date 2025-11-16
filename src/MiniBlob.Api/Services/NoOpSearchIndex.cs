namespace MiniBlob.Api.Services;

using MiniBlob.Api.Data;
using MiniBlob.Api.Models;
using System.Threading.Tasks;

/// <summary>
/// No-op implementation of ISearchIndex used when indexing is disabled.
/// This satisfies the interface but performs no database operations.
/// </summary>
public class NoOpSearchIndex : ISearchIndex
{
    public Task AddOrUpdateAsync(IndexRecord record) {
        // Skip indexing entirely
        return Task.CompletedTask;
    }

    public Task<SearchResult> SearchAsync(string query, int page = 1, int pageSize = 20) {
        // Always return an empty result set
        var empty = new SearchResult(0, 1, 20, []);
        return Task.FromResult(empty);
    }
}