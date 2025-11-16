using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniBlob.Api.Data.Models;

public class BlobMetadata
{
    [Key]
    public int Id { get; set; }
    [Required]
    public string Container { get; set; } = string.Empty;
    [Required]
    public string BlobPath { get; set; } = string.Empty;
    [Required]
    public string FileName { get; set; } = string.Empty;
    [Required]
    public string CreatedBy { get; set; } = string.Empty;
    [Required]
    public DateTime CreatedUtc { get; set; }
    public long Size { get; set; }
    public string ETag { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
