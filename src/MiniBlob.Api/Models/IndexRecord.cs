namespace MiniBlob.Api.Models;
public record IndexRecord(string Container, string BlobPath, string Filename, DateTime CreatedUtc, string CreatedBy, long Size);
