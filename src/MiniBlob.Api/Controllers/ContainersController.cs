using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniBlob.Api.Services;
using System.Text.Json;

namespace MiniBlob.Api.Controllers;

[ApiController]
[Route("containers")]
public class ContainersController : ControllerBase
{
    private readonly IStorageService _storage;
    private readonly FileMetadataAuthService _auth;
    private readonly ILogger<ContainersController> _logger;

    public ContainersController(
        IStorageService storage,
        FileMetadataAuthService auth,
        ILogger<ContainersController> logger) {
        _storage = storage;
        _auth = auth;
        _logger = logger;
    }

    public record CreateContainerRequest(
        List<string>? UsersAllowed,
        List<string>? RolesAllowed
    );

    /// <summary>
    /// Creates a new container with an initial .container.auth file.
    /// Only admins can create containers.
    /// </summary>
    [HttpPost("{container}")]
    [Authorize(Roles = FileMetadataAuthService.ROLE_ADMIN)]
    public async Task<IActionResult> CreateContainer(string container, [FromBody] CreateContainerRequest? request) {
        if (string.IsNullOrWhiteSpace(container))
            return BadRequest("Container name is required.");

        try {
            var containerPath = _storage.FileSystemPath(container, "");

            if (System.IO.Directory.Exists(containerPath))
                return Conflict("Container already exists.");

            System.IO.Directory.CreateDirectory(containerPath);

            var currentUser = User.Identity?.Name ?? "admin";

            List<string> roles = request?.RolesAllowed ?? [ FileMetadataAuthService.ROLE_ADMIN ];
            if (!roles.Contains(FileMetadataAuthService.ROLE_ADMIN, StringComparer.OrdinalIgnoreCase)) {
                roles.Add(FileMetadataAuthService.ROLE_ADMIN);
            }
            // Create AuthInfo for the container
            var info = new FileMetadataAuthService.AuthInfo(
                Owner: currentUser,
                UsersAllowed: request?.UsersAllowed ?? new List<string> { currentUser },
                RolesAllowed: roles,
                CreatedUtc: DateTime.UtcNow,
                CreatedBy: currentUser
            );

            // Save .container.auth file
            var authPath = System.IO.Path.Combine(containerPath, ".container.auth");
            await System.IO.File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

            _logger.LogInformation("Container {Container} created by {User}", container, currentUser);

            var response = new {
                Container = container,
                CreatedBy = currentUser,
                CreatedUtc = info.CreatedUtc,
                UsersAllowed = info.UsersAllowed,
                RolesAllowed = info.RolesAllowed
            };

            return CreatedAtAction(nameof(GetContainerInfo), new { container }, response);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to create container {Container}", container);
            return StatusCode(500, "Failed to create container");
        }
    }

    [HttpGet("{container}")]
    [Authorize]
    public IActionResult GetContainerInfo(string container) {
        var containerPath = _storage.FileSystemPath(container, "");
        if (!System.IO.Directory.Exists(containerPath))
            return NotFound();

        var authPath = System.IO.Path.Combine(containerPath, ".container.auth");
        FileMetadataAuthService.AuthInfo? info = null;
        if (System.IO.File.Exists(authPath)) {
            var json = System.IO.File.ReadAllText(authPath);
            info = JsonSerializer.Deserialize<FileMetadataAuthService.AuthInfo>(json);
        }

        var response = new {
            Container = container,
            Exists = true,
            AuthInfo = info
        };

        return Ok(response);
    }
}
