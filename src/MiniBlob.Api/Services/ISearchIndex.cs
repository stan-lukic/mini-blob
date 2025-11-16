using MiniBlob.Api.Data;
using MiniBlob.Api.Models;

namespace MiniBlob.Api.Services;
public interface ISearchIndex
{
    Task AddOrUpdateAsync(IndexRecord rec);
    Task<SearchResult> SearchAsync(string query, int page, int pageSize);
}