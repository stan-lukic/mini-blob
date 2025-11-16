using System.Security.Claims;
using System.Text.Json;

namespace MiniBlob.Api.Services;
public class FileMetadataAuthService
{
    public const string USER_ADMIN = "admin";
    public const string ROLE_ADMIN = "admin";

    public record AuthInfo(
        string Owner,
        List<string> RolesAllowed,
        List<string> UsersAllowed,
        DateTime CreatedUtc,
        string CreatedBy,
        string Access = "private" // "public" or "private"
    )
    {
        public static AuthInfo FromMetadata(IDictionary<string, string> meta, string userName) {
            var rolesAllowed = new List<string>();
            var usersAllowed = new List<string>();

            if (meta.TryGetValue("roles", out var roles))
                rolesAllowed.AddRange(roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (meta.TryGetValue("users", out var users))
                usersAllowed.AddRange(users.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var access = "private";
            if (meta.TryGetValue("access", out var pub) && pub.Equals("public", StringComparison.OrdinalIgnoreCase))
                access = "public";

            // Always include owner
            if (!usersAllowed.Contains(userName, StringComparer.OrdinalIgnoreCase))
                usersAllowed.Add(userName);

            return new AuthInfo(
                Owner: userName,
                RolesAllowed: rolesAllowed.Count > 0 ? rolesAllowed : new List<string> { FileMetadataAuthService.ROLE_ADMIN },
                UsersAllowed: usersAllowed,
                CreatedUtc: DateTime.UtcNow,
                CreatedBy: userName,
                Access: access
            );
        }
    };

    private readonly ILogger<FileMetadataAuthService> _logger;

    public FileMetadataAuthService(ILogger<FileMetadataAuthService> logger) {
        _logger = logger;
    }

    public void EnsureAuthFile(string blobPath, string createdBy, IDictionary<string, string>? metadata = null) {
        var authPath = blobPath + ".auth";
        if (File.Exists(authPath)) return;

        var access = "private";
        if (metadata != null && metadata.TryGetValue("access", out var accessValue) &&
            accessValue.Equals("public", StringComparison.OrdinalIgnoreCase)) {
            access = "public";
        }

        var info = new AuthInfo(
            Owner: createdBy,
            RolesAllowed: new List<string> { ROLE_ADMIN },
            UsersAllowed: new List<string> { createdBy },
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: createdBy,
            Access: access
        );

        File.WriteAllText(authPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<bool> CanReadAsync(string fileAuthPath, ClaimsPrincipal user) {
        try {
            var authPath = fileAuthPath;

            // Fallback to container auth if missing
            if (!File.Exists(authPath)) {
                var containerPath = Path.GetDirectoryName(fileAuthPath)!;
                var containerAuthPath = Path.Combine(containerPath, ".container.auth");
                if (File.Exists(containerAuthPath))
                    authPath = containerAuthPath;
            }

            if (!File.Exists(authPath))
                return user.IsInRole(ROLE_ADMIN);

            var json = await File.ReadAllTextAsync(authPath);
            var info = JsonSerializer.Deserialize<AuthInfo>(json);
            if (info == null)
                return user.IsInRole(ROLE_ADMIN);

            var userName = user.Identity?.Name ?? "";

            // allow anyone if Access = "public"
            if (info.Access.Equals("public", StringComparison.OrdinalIgnoreCase))
                return true;

            if (info.UsersAllowed.Contains(userName, StringComparer.OrdinalIgnoreCase))
                return true;

            if (info.RolesAllowed.Any(role => user.IsInRole(role)))
                return true;

            return info.Owner.Equals(userName, StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Auth check failed for {Path}", fileAuthPath);
            return false;
        }
    }

    public async Task<bool> CanWriteAsync(string fileAuthPath, ClaimsPrincipal user) {
        try {
            var authPath = fileAuthPath;

            if (!File.Exists(authPath)) {
                var containerPath = Path.GetDirectoryName(fileAuthPath)!;
                var containerAuthPath = Path.Combine(containerPath, ".container.auth");
                if (File.Exists(containerAuthPath))
                    authPath = containerAuthPath;
            }

            if (!File.Exists(authPath))
                return user.IsInRole(ROLE_ADMIN);

            var json = await File.ReadAllTextAsync(authPath);
            var info = JsonSerializer.Deserialize<AuthInfo>(json);
            if (info == null)
                return user.IsInRole(ROLE_ADMIN);

            var userName = user.Identity?.Name ?? "";
            return info.Owner.Equals(userName, StringComparison.OrdinalIgnoreCase)
                   || user.IsInRole(ROLE_ADMIN);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Auth check failed for {Path}", fileAuthPath);
            return false;
        }
    }

    public void EnsureContainerAuthFile(string containerPath, string createdBy) {
        var authPath = Path.Combine(containerPath, ".container.auth");
        if (File.Exists(authPath)) return;

        var info = new AuthInfo(
            Owner: createdBy,
            RolesAllowed: new() { ROLE_ADMIN },
            UsersAllowed: new() { createdBy },
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: createdBy
        );

        File.WriteAllText(authPath,
            JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    }


    public async Task EnsureAuthFileAsync(string filePath, string createdBy, IDictionary<string, string>? metadata = null) {
        var authPath = filePath + ".auth";

        if (File.Exists(authPath))
            return; // already exists, skip

        try {
            FileMetadataAuthService.AuthInfo info;

            if (metadata != null && metadata.Count > 0) {
                // Create based on metadata headers like x-ms-meta-public, etc.
                info = FileMetadataAuthService.AuthInfo.FromMetadata(metadata, createdBy);
            } else {
                // Default fallback: admin-only + owner
                info = new FileMetadataAuthService.AuthInfo(
                    Owner: createdBy,
                    RolesAllowed: new List<string> { FileMetadataAuthService.ROLE_ADMIN },
                    UsersAllowed: new List<string> { createdBy },
                    CreatedUtc: DateTime.UtcNow,
                    CreatedBy: createdBy
                );
            }

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(authPath, json);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to create .auth file for {Path}", filePath);
        }
    }


    public static AuthInfo AuthInfoFromMetadata(string userName, IDictionary<string, string>? meta) {
        var rolesAllowed = new List<string>();
        var usersAllowed = new List<string>();
        var accessLevel = "private";
        if (meta != null) {
            if (meta.TryGetValue("roles", out var roles) && !string.IsNullOrWhiteSpace(roles)) {
                rolesAllowed.AddRange(roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            if (meta.TryGetValue("users", out var users) && !string.IsNullOrWhiteSpace(users)) {
                usersAllowed.AddRange(users.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            // "access" = "public"
            if (meta.TryGetValue("access", out var access) && access.Equals("public", StringComparison.OrdinalIgnoreCase)) {
                if (!rolesAllowed.Contains("public", StringComparer.OrdinalIgnoreCase))
                    accessLevel = "public";
            }
        }

        // Ensure owner is present in usersAllowed
        if (!string.IsNullOrWhiteSpace(userName) && !usersAllowed.Contains(userName, StringComparer.OrdinalIgnoreCase))
            usersAllowed.Add(userName);

        // If no roles specified, default to admin role (keeps previous behaviour)
        if (rolesAllowed.Count == 0)
            rolesAllowed.Add(ROLE_ADMIN);

        return new AuthInfo(
            Owner: userName,
            RolesAllowed: rolesAllowed,
            UsersAllowed: usersAllowed,
            CreatedUtc: DateTime.UtcNow,
            CreatedBy: userName,
            Access: accessLevel
        );
    }

}

//public class FileMetadataAuthService
//{
//    public const string USER_ADMIN = "admin";
//    public const string ROLE_ADMIN = "admin";

//    public record AuthInfo(
//        string Owner,
//        List<string> RolesAllowed,
//        List<string> UsersAllowed,
//        DateTime CreatedUtc,
//        string CreatedBy
//    );



//    public static AuthInfo AuthInfoFromMetadata(string userName, IDictionary<string, string> meta) {
//        var rolesAllowed = new List<string>();
//        var usersAllowed = new List<string>();

//        if (meta.TryGetValue("roles", out var roles))
//            rolesAllowed.AddRange(roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

//        if (meta.TryGetValue("users", out var users))
//            usersAllowed.AddRange(users.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

//        // Handle public access
//        if (meta.TryGetValue("public", out var pub) && pub.Equals("true", StringComparison.OrdinalIgnoreCase))
//            rolesAllowed.Add("public");

//        // Always include owner
//        if (!usersAllowed.Contains(userName, StringComparer.OrdinalIgnoreCase))
//            usersAllowed.Add(userName);

//        // If no roles specified, default to admin
//        if (rolesAllowed.Count == 0)
//            rolesAllowed.Add(FileMetadataAuthService.ROLE_ADMIN);

//        return new AuthInfo(
//            Owner: userName,
//            RolesAllowed: rolesAllowed,
//            UsersAllowed: usersAllowed,
//            CreatedUtc: DateTime.UtcNow,
//            CreatedBy: userName
//        );
//    }


//    private readonly ILogger<FileMetadataAuthService> _logger;

//    public FileMetadataAuthService(ILogger<FileMetadataAuthService> logger) {
//        _logger = logger;
//    }

//    public async Task<bool> CanReadAsync(string fileAuthPath, ClaimsPrincipal user) {
//        try {
//            var authPath = fileAuthPath;

//            // Fallback to container auth if file auth doesn't exist
//            if (!File.Exists(authPath)) {
//                var containerPath = Path.GetDirectoryName(fileAuthPath)!;
//                var containerAuthPath = Path.Combine(containerPath, ".container.auth");
//                if (File.Exists(containerAuthPath))
//                    authPath = containerAuthPath;
//            }

//            if (!File.Exists(authPath))
//                return user.IsInRole(ROLE_ADMIN); // fallback if neither exists

//            var json = await File.ReadAllTextAsync(authPath);
//            var info = JsonSerializer.Deserialize<AuthInfo>(json);
//            if (info == null)
//                return user.IsInRole(ROLE_ADMIN);

//            var userName = user.Identity?.Name ?? "";

//            if (info.UsersAllowed.Contains(userName, StringComparer.OrdinalIgnoreCase))
//                return true;

//            if (info.RolesAllowed.Any(role => user.IsInRole(role)))
//                return true;

//            // Owner can always read
//            return info.Owner.Equals(userName, StringComparison.OrdinalIgnoreCase);
//        } catch (Exception ex) {
//            _logger.LogWarning(ex, "Auth check failed for {Path}", fileAuthPath);
//            return false;
//        }
//    }

//    public async Task<bool> CanWriteAsync(string fileAuthPath, ClaimsPrincipal user) {
//        try {
//            var authPath = fileAuthPath;

//            // Fallback to container auth if file auth doesn't exist
//            if (!File.Exists(authPath)) {
//                var containerPath = Path.GetDirectoryName(fileAuthPath)!;
//                var containerAuthPath = Path.Combine(containerPath, ".container.auth");
//                if (File.Exists(containerAuthPath))
//                    authPath = containerAuthPath;
//            }

//            if (!File.Exists(authPath))
//                return user.IsInRole(ROLE_ADMIN); // fallback

//            var json = await File.ReadAllTextAsync(authPath);
//            var info = JsonSerializer.Deserialize<AuthInfo>(json);
//            if (info == null)
//                return user.IsInRole(ROLE_ADMIN);

//            var userName = user.Identity?.Name ?? "";

//            // Only owner or admin can write
//            return info.Owner.Equals(userName, StringComparison.OrdinalIgnoreCase) || user.IsInRole(ROLE_ADMIN);
//        } catch (Exception ex) {
//            _logger.LogWarning(ex, "Auth check failed for {Path}", fileAuthPath);
//            return false;
//        }
//    }


//    private static string? FindContainerDirectory(string authPath) {
//        // Walk up the tree to find the container root (stop if "container" directory found)
//        var dir = Path.GetDirectoryName(authPath);
//        while (!string.IsNullOrEmpty(dir)) {
//            var containerAuth = Path.Combine(dir, ".container.auth");
//            if (File.Exists(containerAuth))
//                return dir;

//            var parent = Directory.GetParent(dir);
//            if (parent == null) break;
//            dir = parent.FullName;
//        }

//        return null;
//    }

//    public void EnsureAuthFile(string blobPath, string createdBy) {
//        var authPath = blobPath + ".auth";
//        if (File.Exists(authPath)) return;

//        var info = new AuthInfo(
//            Owner: createdBy,
//            RolesAllowed: new List<string> { ROLE_ADMIN },
//            UsersAllowed: new List<string> { createdBy },
//            CreatedUtc: DateTime.UtcNow,
//            CreatedBy: createdBy
//        );

//        File.WriteAllText(authPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
//    }
//    public void EnsureContainerAuthFile(string containerPath, string createdBy) {
//        var authPath = Path.Combine(containerPath, ".container.auth");
//        if (File.Exists(authPath)) return;

//        var info = new AuthInfo(
//            Owner: createdBy,
//            RolesAllowed: new() { ROLE_ADMIN },
//            UsersAllowed: new() { createdBy },
//            CreatedUtc: DateTime.UtcNow,
//            CreatedBy: createdBy
//        );

//        File.WriteAllText(authPath,
//            JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
//    }

//}
