using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiniBlob.Api.Models;
using MiniBlob.Api.Services;
using System.Text.Json;

[ApiController]
[Route("{container}/{**blobPath}")]
public class BlobController : ControllerBase
{
    private readonly IStorageService _storage;
    private readonly ISearchIndex _index;
    private readonly ILogger<BlobController> _logger;
    private readonly FileMetadataAuthService _auth;

    public IOptions<MiniBlobOptions> _options { get; }

    public BlobController(
        IStorageService storage,
        ISearchIndex index,
        ILogger<BlobController> logger,
        FileMetadataAuthService auth,
        IOptions<MiniBlobOptions> options) {
        _storage = storage;
        _index = index;
        _logger = logger;
        _auth = auth;
        _options = options;
    }

    private string ContainerAuthPath(string container) {
        var path = Path.Combine(_storage.FileSystemPath(container, ""), ".container.auth");
        return path;
    }

    private async Task<bool> CheckContainerAuthAsync(string container) {
        var containerAuth = ContainerAuthPath(container);
        if (!System.IO.File.Exists(containerAuth)) {
            _logger.LogWarning("Container auth file missing for {Container}", container);
            return false;
        }

        return await _auth.CanReadAsync(containerAuth, User);
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> Put(string container, string blobPath, [FromQuery] string? comp = null) {

        if (!await CheckContainerAuthAsync(container))
            return Forbid();

        var filePath = _storage.FileSystemPath(container, blobPath);
        var containerAuthPath = Path.Combine(_storage.FileSystemPath(container, ""), ".container.auth");
        var fileAuthPath = filePath + ".auth";

        // Check if file auth is required
        bool createFileAuth = false;

        if (!System.IO.File.Exists(containerAuthPath)) {
            // No container auth -> file needs its own auth
            createFileAuth = true;
        }

        // Collect metadata headers
        var meta = Request.Headers
            .Where(h => h.Key.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key["x-ms-meta-".Length..], h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        if (Request.Headers.ContainsKey("x-ms-meta-public") ||
            Request.Headers.ContainsKey("x-ms-meta-roles") ||
            Request.Headers.ContainsKey("x-ms-meta-users")) {
            // Special headers indicate file-specific rules ? create file auth
            createFileAuth = true;
        }

        // Enforce write permission
        if (System.IO.File.Exists(fileAuthPath)) {
            if (!await _auth.CanWriteAsync(fileAuthPath, User))
                return Forbid();
        }

        // Handle metadata-only PUT
        if (!string.IsNullOrEmpty(comp) && comp.Equals("metadata", StringComparison.OrdinalIgnoreCase)) {
            await _storage.UpdateMetadataAsync(container, blobPath, meta);
            return Ok();
        }

        Response.RegisterForDispose(Request.Body);

        var userName = User?.Identity?.Name ?? "anonymous";

        // Save blob
        await _storage.SaveBlobAsync(container, blobPath, Request.Body, meta, userName);

        // Create [file].auth if required
        if (createFileAuth && !System.IO.File.Exists(fileAuthPath)) {
            var info = FileMetadataAuthService.AuthInfoFromMetadata(userName, meta);
            await System.IO.File.WriteAllTextAsync(fileAuthPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }

        var enableIndexing = _options.Value.EnableSearchIndexing;
        if (enableIndexing && _index != null) {
            try {
                var maybeBlob = await _storage.GetBlobAsync(container, blobPath);
                if (maybeBlob != null) {
                    var blob = maybeBlob.Value;
                    await _index.AddOrUpdateAsync(
                        new IndexRecord(container, blobPath, Path.GetFileName(blobPath),
                                        blob.LastModified.ToUniversalTime(), userName, blob.Size));
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Indexing failed");
            }
        }

        return Created($"{Request.Scheme}://{Request.Host}/{container}/{blobPath}", null);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get(string container, string blobPath) {
        if (!await CheckContainerAuthAsync(container))
            return Forbid();

        var filePath = _storage.FileSystemPath(container, blobPath);

        if (!await _auth.CanReadAsync(filePath + ".auth", User))
            return Forbid();

        var maybeBlob = await _storage.GetBlobAsync(container, blobPath);
        if (maybeBlob == null)
            return NotFound();

        var (stream, metadata, lastModified, etag, size) = maybeBlob.Value;
        if (stream == null)
            return NotFound();

        var headers = Response.GetTypedHeaders();
        headers.ETag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
        headers.LastModified = lastModified.ToUniversalTime();
        Response.ContentLength = size;

        Response.GetTypedHeaders().CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue {
            Public = true,
            MaxAge = TimeSpan.FromHours(1)
        };

        foreach (var kv in metadata)
            Response.Headers[$"x-ms-meta-{kv.Key}"] = kv.Value;

        return File(stream, "application/octet-stream", Path.GetFileName(blobPath));
    }

    [HttpHead]
    [Authorize]
    public async Task<IActionResult> Head(string container, string blobPath) {
        if (!await CheckContainerAuthAsync(container))
            return Forbid();

        var meta = await _storage.GetBlobMetadataAsync(container, blobPath);
        if (meta == null) return NotFound();

        foreach (var kv in meta)
            Response.GetTypedHeaders().Append($"x-ms-meta-{kv.Key}", kv.Value);

        var maybeBlob = await _storage.GetBlobAsync(container, blobPath);
        if (maybeBlob != null) {
            var blob = maybeBlob.Value;
            Response.GetTypedHeaders().ETag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{blob.ETag}\"");
            Response.GetTypedHeaders().LastModified = blob.LastModified;
            Response.Headers.ContentLength = blob.Size;
            Response.Headers.ContentType = "application/octet-stream";
        }

        return Ok();
    }
}