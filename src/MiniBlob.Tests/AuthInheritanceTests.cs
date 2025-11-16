using MiniBlob.Api.Services;
using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MiniBlob.Tests;

public class AuthInheritanceTests
{
    private readonly FileMetadataAuthService _auth;
    private static readonly  JsonSerializerOptions WriteIndentedJson = new() { WriteIndented = true };
    public AuthInheritanceTests() {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<FileMetadataAuthService>();
        _auth = new FileMetadataAuthService(logger);
    }

    private static ClaimsPrincipal User(string name, string role) {
        return new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role)
        ], "mock"));
    }

    private static string CreateAuthFile(FileMetadataAuthService.AuthInfo info) {
        var tmp = Path.GetTempFileName();
        File.Delete(tmp);
        var authPath = tmp + ".auth";
        File.WriteAllText(authPath, JsonSerializer.Serialize(info, WriteIndentedJson));
        return authPath;
    }

    [Fact]
    public async Task FileWithoutAuth_UsesContainerAuth_ForAccess() {
        // Arrange
        var rootDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(rootDir);

        var containerAuthPath = Path.Combine(rootDir, ".container.auth");
        var filePath = Path.Combine(rootDir, "file1.txt");
        await File.WriteAllTextAsync(filePath, "test");

        // Create a container-level auth allowing only "alice"
        var info = new FileMetadataAuthService.AuthInfo(
            Owner: "alice",
            RolesAllowed: [FileMetadataAuthService.ROLE_ADMIN],
            UsersAllowed: ["alice"],
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: "alice"
        );
        await File.WriteAllTextAsync(containerAuthPath, JsonSerializer.Serialize(info, WriteIndentedJson));

        var owner = User("alice", "User");
        var stranger = User("bob", "User");

        // Act
        var canOwnerRead = await _auth.CanReadAsync(filePath + ".auth", owner);
        var canStrangerRead = await _auth.CanReadAsync(filePath + ".auth", stranger);

        // Assert
        Assert.True(canOwnerRead);    // inherits access from container
        Assert.False(canStrangerRead); // denied
    }

    [Fact]
    public async Task ContainerAuth_AllowsAdminFallback() {
        // Arrange
        var rootDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(rootDir);

        var containerAuthPath = Path.Combine(rootDir, ".container.auth");
        await File.WriteAllTextAsync(containerAuthPath, """
        {
          "Owner": "system",
          "RolesAllowed": ["admin"],
          "UsersAllowed": [],
          "CreatedUtc": "2025-11-09T00:00:00Z",
          "CreatedBy": "system"
        }
        """);

        var admin = User("root", "admin");
        var regular = User("joe", "User");
        var filePath = Path.Combine(rootDir, "doc.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        // Act
        var adminCanRead = await _auth.CanReadAsync(filePath + ".auth", admin);
        var regularCanRead = await _auth.CanReadAsync(filePath + ".auth", regular);

        // Assert
        Assert.True(adminCanRead);
        Assert.False(regularCanRead);
    }

    [Fact]
    public async Task MissingAuth_CompletelyDefaultsToAdmin() {
        // Arrange
        var rootDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(rootDir);

        var filePath = Path.Combine(rootDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var admin = User("super", "admin");
        var user = User("bob", "User");

        // Act
        var adminCanRead = await _auth.CanReadAsync(filePath + ".auth", admin);
        var userCanRead = await _auth.CanReadAsync(filePath + ".auth", user);

        // Assert
        Assert.True(adminCanRead);   // no auth -> admins always allowed
        Assert.False(userCanRead);   // others denied
    }
}
