namespace MiniBlob.Api.Services;

using System.Security.Claims;

public interface IStorageAuthService
{
    Task<bool> CanReadAsync(string container, string blobPath, ClaimsPrincipal user);
    Task<bool> CanWriteAsync(string container, string blobPath, ClaimsPrincipal user);
}
