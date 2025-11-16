using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using global::MiniBlob.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MiniBlob.Tests;

public class FileMetadataAuthServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileMetadataAuthService _auth;

    public FileMetadataAuthServiceTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "MiniBlobAuthTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _auth = new FileMetadataAuthService(NullLogger<FileMetadataAuthService>.Instance);
    }

    public void Dispose() {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private static ClaimsPrincipal User(string name, string? role = null, string? dept = null) {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (role != null) claims.Add(new(ClaimTypes.Role, role));
        if (dept != null) claims.Add(new("department", dept));
        var identity = new ClaimsIdentity(claims, "mock");
        return new ClaimsPrincipal(identity);
    }

    private string CreateAuthFile(object data) {
        var authPath = Path.Combine(_tempDir, Guid.NewGuid() + ".auth");
        File.WriteAllText(authPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return authPath;
    }

    [Fact]
    public async Task AdminAlwaysAllowed() {
        var authPath = Path.Combine(_tempDir, "file.auth");
        _auth.EnsureAuthFile(authPath.Replace(".auth", ""), "someone");
        var admin = User("super", FileMetadataAuthService.ROLE_ADMIN);

        Assert.True(await _auth.CanReadAsync(authPath, admin));
        Assert.True(await _auth.CanWriteAsync(authPath, admin));
    }

    [Fact]
    public async Task NoMetadata_AllowsOnlyAdminRead() {
        var authPath = Path.Combine(_tempDir, "noauth.auth");
        var normalUser = User("tester", "user");
        var adminUser = User("admin", FileMetadataAuthService.ROLE_ADMIN);

        Assert.False(await _auth.CanReadAsync(authPath, normalUser));
        Assert.True(await _auth.CanReadAsync(authPath, adminUser));
        Assert.False(await _auth.CanWriteAsync(authPath, normalUser));
        Assert.True(await _auth.CanWriteAsync(authPath, adminUser));
    }



    [Fact]
    public async Task OwnerCanReadAndWrite() {
        var authPath = CreateAuthFile(new FileMetadataAuthService.AuthInfo(
            Owner: "owner",
            RolesAllowed: ["admin" ],
            UsersAllowed: [],
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: "owner"
        ));

        var owner = User("owner", "User");
        Assert.True(await _auth.CanReadAsync(authPath, owner));
        Assert.True(await _auth.CanWriteAsync(authPath, owner));
    }

    [Fact]
    public async Task DepartmentClaim_AllowsRead() {
        // We're simulating department access by using a role "HR"
        var authPath = CreateAuthFile(new FileMetadataAuthService.AuthInfo(
            Owner: "someone",
            RolesAllowed: new List<string> { "HR" },
            UsersAllowed: new List<string>(),
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: "someone"
        ));

        // Give the user the HR role so user.IsInRole("HR") returns true
        var user = User("tester", role: "HR");
        Assert.True(await _auth.CanReadAsync(authPath, user));
    }

    [Fact]
    public async Task RoleBasedAccess_AllowsRead() {
        var authPath = CreateAuthFile(new FileMetadataAuthService.AuthInfo(
            Owner: "someone",
            RolesAllowed: new List<string> { "HR", "Manager" },
            UsersAllowed: new List<string>(),
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: "someone"
        ));

        var user = User("john", "Manager");
        Assert.True(await _auth.CanReadAsync(authPath, user));
    }



    [Fact]
    public void AuthInfoFromMetadata_NoMetadata_DefaultsToAdminRoleAndOwner() {
        // Arrange
        var userName = "alice";

        // Act
        var info = FileMetadataAuthService.AuthInfoFromMetadata(userName, null);

        // Assert
        Assert.Equal(userName, info.Owner);
        Assert.Contains(userName, info.UsersAllowed);
        Assert.Contains(FileMetadataAuthService.ROLE_ADMIN, info.RolesAllowed);
        Assert.Equal(userName, info.CreatedBy);
        Assert.True(info.CreatedUtc <= DateTime.UtcNow);
    }
    [Fact]
    public void AuthInfoFromMetadata_ParsesUsersAndRolesFromMetadata() {
        // Arrange
        var userName = "bob";
        var meta = new Dictionary<string, string> {
            ["users"] = "john, jane",
            ["roles"] = "manager, editor"
        };

        // Act
        var info = FileMetadataAuthService.AuthInfoFromMetadata(userName, meta);

        // Assert
        Assert.Equal("bob", info.Owner);
        Assert.Contains("john", info.UsersAllowed);
        Assert.Contains("jane", info.UsersAllowed);
        Assert.Contains("bob", info.UsersAllowed); // owner must always be added
        Assert.Contains("manager", info.RolesAllowed);
        Assert.Contains("editor", info.RolesAllowed);
    }
    [Theory]
    [InlineData("access", "public")]
    public void AuthInfoFromMetadata_PublicAccess_AddsPublicRole(string key, string value) {
        // Arrange
        var meta = new Dictionary<string, string> { [key] = value };

        // Act
        var info = FileMetadataAuthService.AuthInfoFromMetadata("chris", meta);

        // Assert
        Assert.Equal("public", info.Access);
        Assert.Contains("chris", info.UsersAllowed);
    }
    [Fact]
    public async Task EnsureAuthFile_RespectsPrivateAccessMetadata() {
        // Arrange
        var tmp = Path.GetTempFileName();
        var authPath = tmp + ".auth";

        if (File.Exists(authPath))
            File.Delete(authPath);

        // Simulate metadata specifying private access
        var metadata = new Dictionary<string, string> {
            ["access"] = "private"
        };

        // Act — create .auth file (should default to owner + admin only)
        await _auth.EnsureAuthFileAsync(tmp, "alice", metadata);

        // Assert — file created
        Assert.True(File.Exists(authPath));

        // Read and verify content
        var json = await File.ReadAllTextAsync(authPath);
        var info = JsonSerializer.Deserialize<FileMetadataAuthService.AuthInfo>(json)!;

        Assert.Contains("alice", info.UsersAllowed);
        Assert.Contains(FileMetadataAuthService.ROLE_ADMIN, info.RolesAllowed);
        Assert.DoesNotContain("public", info.RolesAllowed);
        Assert.Equal("private", info.Access);  

        // Create different users
        var owner = User("alice", "User");
        var stranger = User("bob", "User");
        var admin = User("root", FileMetadataAuthService.ROLE_ADMIN);

        // Verify access logic
        Assert.True(await _auth.CanReadAsync(authPath, owner));    // owner allowed
        Assert.False(await _auth.CanReadAsync(authPath, stranger)); // stranger denied
        Assert.True(await _auth.CanReadAsync(authPath, admin));     // admin allowed

        // Cleanup
        File.Delete(tmp);
        File.Delete(authPath);
    }

    [Fact]
    public async Task EnsureAuthFile_RespectsPublicAccessMetadata() {
        // Arrange
        var tmp = Path.GetTempFileName();
        var authPath = tmp + ".auth";

        if (File.Exists(authPath))
            File.Delete(authPath);

        // Simulate metadata specifying public access
        var metadata = new Dictionary<string, string> {
            ["access"] = "public"
        };

        // Act — create .auth file
        await _auth.EnsureAuthFileAsync(tmp, "alice", metadata);

        // Assert — file created
        Assert.True(File.Exists(authPath));

        // Read and check contents
        var json = await File.ReadAllTextAsync(authPath);
        var info = JsonSerializer.Deserialize<FileMetadataAuthService.AuthInfo>(json)!;

        Assert.Contains("public", info.Access);

        // Verify access logic
        var user = User("randomUser", role: "User");
        var canRead = await _auth.CanReadAsync(authPath, user);

        Assert.True(canRead); // anyone should be able to read

        // Cleanup
        File.Delete(tmp);
        File.Delete(authPath);
    }

    [Fact]
    public async Task PublicAccess_AllowsAnyoneRead() {
        // Arrange — create a public .auth file
        var auth = new FileMetadataAuthService.AuthInfo(
            Owner: "owner",
            RolesAllowed: new List<string> { "admin" },
            UsersAllowed: new List<string>(),
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: "owner",
            Access: "public"
        );
        var authPath = CreateAuthFile(auth);

        var user = User("anyone", "User");

        // Act / Assert
        Assert.True(await _auth.CanReadAsync(authPath, user));
    }

}
