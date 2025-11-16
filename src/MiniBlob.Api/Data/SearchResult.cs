using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniBlob.Api.Models;

namespace MiniBlob.Api.Data;

public record SearchResult(int TotalCount, int Page, int PageSize, IEnumerable<IndexRecord> Records);
