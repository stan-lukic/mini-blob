namespace MiniBlob.Api.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;


//public interface IStorageService
//{
//    Task SaveBlobAsync(string container, string blobPath, Stream content, IDictionary<string, string> metadata, string createdBy);
//    Task<(Stream? Stream, IDictionary<string, string> Metadata, DateTime LastModified, string ETag, long Size)?> GetBlobAsync(string container, string blobPath);
//    Task<IDictionary<string, string>?> GetBlobMetadataAsync(string container, string blobPath);
//    Task UpdateMetadataAsync(string container, string blobPath, IDictionary<string, string> metadata);
//    string FileSystemPath(string container, string blobPath);
//}


public interface IStorageService
{
    /// <summary>
    /// Saves a blob (file) with metadata and creator information.
    /// </summary>
    Task SaveBlobAsync(
        string container,
        string blobPath,
        Stream content,
        IDictionary<string, string> metadata,
        string createdBy);

    /// <summary>
    /// Gets a blob stream, metadata, and system info (ETag, size, last modified).
    /// </summary>
    Task<(Stream? Stream, IDictionary<string, string> Metadata, DateTime LastModified, string ETag, long Size)?>
        GetBlobAsync(string container, string blobPath);

    /// <summary>
    /// Gets only the metadata for a blob.
    /// </summary>
    Task<IDictionary<string, string>?> GetBlobMetadataAsync(string container, string blobPath);

    /// <summary>
    /// Updates metadata for a blob.
    /// </summary>
    Task UpdateMetadataAsync(string container, string blobPath, IDictionary<string, string> metadata);

    /// <summary>
    /// Resolves full system path for the blob (useful for internal ops).
    /// </summary>
    string FileSystemPath(string container, string blobPath);
}
