using System.Text;
using System.Text.Json;
using MiniBlob.Api.Models;

namespace MiniBlob.Api.Services;

public record MiniBlobOptions
{
    public string RootPath { get; init; } = "MiniBlobStorage";
    public bool EnableSearchIndexing { get; init; } = false;
}

public class FileSystemStorageService : IStorageService
{

    private readonly MiniBlobOptions _options;
    private readonly ILogger<FileSystemStorageService> _logger;
    private readonly FileMetadataAuthService _auth;

    public FileSystemStorageService(
        Microsoft.Extensions.Options.IOptions<MiniBlobOptions> options,
        ILogger<FileSystemStorageService> logger, FileMetadataAuthService auth)
    {
        _options = options.Value;
        _logger = logger;
        _auth = auth;
    }

    private string ContainerPath(string container) => Path.Combine(_options.RootPath, container);

    private string BlobFilePath(string container, string blobPath) => Path.Combine(ContainerPath(container), blobPath.Replace('/', Path.DirectorySeparatorChar));

    public string FileSystemPath(string container, string blobPath) => BlobFilePath(container, blobPath);

    private string PropPath(string blobFilePath) => blobFilePath + ".prop";



    public async Task SaveBlobAsync(string container, string blobPath, Stream content,
                                IDictionary<string, string> metadata, string createdBy)
    {
        var filePath = BlobFilePath(container, blobPath);
        var dir = Path.GetDirectoryName(filePath)!;

        //Instead of Directory.CreateDirectory(dir), try async to prevent blocking? 
        await Task.Run(() => Directory.CreateDirectory(dir));

        var fileOptions = FileOptions.Asynchronous;
        if (File.Exists(filePath))
        {
            fileOptions |= FileOptions.DeleteOnClose;  // Atomic overwrite (deletes on stream close if unchanged)
        }

        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 65536,  // Optimal for SSD writes
            Options = fileOptions
            //,PreallocationSize = (int?)content.Length  // Optional: Pre-allocate for large streams (~14 MB in your tests)
        };
        await using var fs = new FileStream(filePath, streamOptions);
        await content.CopyToAsync(fs);

        var sys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdBy"] = createdBy,
            ["createdUtc"] = DateTime.UtcNow.ToString("o"),
            ["fileName"] = Path.GetFileName(filePath)
        };
        foreach (var kv in metadata) sys[kv.Key] = kv.Value;
        await File.WriteAllTextAsync(PropPath(filePath), JsonSerializer.Serialize(sys));

    }

    public Task<(Stream? Stream, IDictionary<string, string> Metadata, DateTime LastModified, string ETag, long Size)?>
        GetBlobAsync(string container, string blobPath)
    {
        var filePath = BlobFilePath(container, blobPath);
        if (!File.Exists(filePath))
            return Task.FromResult<(Stream?, IDictionary<string, string>, DateTime, string, long)?>(null);

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
        var meta = GetMetadataInternal(filePath) ?? new Dictionary<string, string>();
        var etag = ComputeETag(filePath);
        FileInfo fi = new(filePath);
        var size = fi.Length;
        var last = fi.LastWriteTimeUtc;

        return Task.FromResult<(Stream?, IDictionary<string, string>, DateTime, string, long)?>(
            (stream, meta, last, etag, size));
    }


    public Task<IDictionary<string, string>?> GetBlobMetadataAsync(string container, string blobPath)
    {
        var filePath = BlobFilePath(container, blobPath);
        if (!File.Exists(filePath)) return Task.FromResult<IDictionary<string, string>?>(null);
        return Task.FromResult<IDictionary<string, string>?>(GetMetadataInternal(filePath));
    }

    public Task UpdateMetadataAsync(string container, string blobPath, IDictionary<string, string> metadata)
    {
        var filePath = BlobFilePath(container, blobPath);
        if (!File.Exists(filePath)) throw new FileNotFoundException();
        var existing = GetMetadataInternal(filePath) ?? new Dictionary<string, string>();
        foreach (var kv in metadata) existing[kv.Key] = kv.Value;
        return File.WriteAllTextAsync(PropPath(filePath), JsonSerializer.Serialize(existing));
    }

    private IDictionary<string, string>? GetMetadataInternal(string filePath)
    {
        var p = PropPath(filePath);
        if (!File.Exists(p)) return null;
        var text = File.ReadAllText(p);
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
            return dict ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }
    public string? GetPhysicalPath(string container, string blobPath)
    {
        var path = BlobFilePath(container, blobPath);
        return File.Exists(path) ? path : null;
    }

    private static string ComputeETag(string filePath)
    {
        var info = new FileInfo(filePath);
        var tag = $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(tag));
    }
}



//private string ComputeETag(string filePath)
//{
//    using var sha = System.Security.Cryptography.SHA256.Create();
//    using var fs = File.OpenRead(filePath);
//    var hash = sha.ComputeHash(fs);
//    return Convert.ToHexString(hash);
//}

//public async Task SaveBlobAsync(string container, string blobPath, Stream content, IDictionary<string,string> metadata, string createdBy)
//{
//    var filePath = BlobFilePath(container, blobPath);
//    var dir = Path.GetDirectoryName(filePath)!;
//    Directory.CreateDirectory(dir);

//    var tmp = filePath + ".tmp";
//    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
//    {
//        await content.CopyToAsync(fs);
//    }
//    if (File.Exists(filePath)) File.Delete(filePath);
//    File.Move(tmp, filePath);

//    var sys = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
//    {
//        ["createdBy"] = createdBy,
//        ["createdUtc"] = DateTime.UtcNow.ToString("o"),
//        ["fileName"] = Path.GetFileName(filePath)
//    };
//    foreach(var kv in metadata) sys[kv.Key] = kv.Value;
//    await File.WriteAllTextAsync(PropPath(filePath), JsonSerializer.Serialize(sys));
//}