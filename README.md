# MiniBlob

A lightweight, self-hosted blob storage API powered by an HTTP server, utilizing the local file system for efficient, scalable object storage. Featuring granular role-based access control with JWT authentication, asynchronous I/O for high-throughput uploads, and experimental search indexing, it is built with ASP.NET Core for seamless integration into .NET ecosystems.

## Features

- **File System Storage** - Simple, performant local file storage
- **Container-Based Organization** - Logical grouping of blobs with hierarchical paths
- **Role-Based Access Control** - Fine-grained permissions using roles and user lists
- **Public/Private Access** - Support for both authenticated and public file access
- **Metadata Support** - Custom metadata headers compatible with Azure Blob Storage (`x-ms-meta-*`)
- **Search Integration** - Optional Entity Framework-based search indexing
- **JWT Authentication** - Secure token-based authentication
- **ETag & Caching** - HTTP caching support with ETags and Last-Modified headers

## Prerequisites

- .NET 10.0 or later
- Entity Framework Core (for search indexing)

## Installation

1. Clone the repository:
```bash
git clone https://github.com/yourusername/miniblob.git
cd miniblob
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Configure application settings in `appsettings.json`:
```json
{
  "MiniBlob": {
    "RootPath": "/var/MiniBlobStorage"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "mini-blob",
    "Audience": "mini-blob-audience"
  }
}
```

4. Run the application:
```bash
dotnet run --project MiniBlob.Api
```

## Architecture

### Core Components

- **IStorageService** - File system operations (save, retrieve, metadata)
- **FileMetadataAuthService** - Authorization logic for containers and blobs
- **ISearchIndex** - Optional indexing for blob search
- **BlobController** - REST API endpoints for blob operations
- **ContainersController** - Container management endpoints

### Security Model

MiniBlob uses a two-tier authorization model:

1. **Container-Level Auth** (`.container.auth`)
   - Controls access to entire containers
   - Falls back to admin-only if missing

2. **File-Level Auth** (`<filename>.auth`)
   - Overrides container permissions for specific files
   - Supports public/private access modes
   - Inherits from container if not present

## API Usage

### Authentication

All requests require a JWT Bearer token:

```http
Authorization: Bearer <your-jwt-token>
```

### Create a Container

Only admins can create containers:

```http
POST /containers/{container}
Content-Type: application/json

{
  "UsersAllowed": ["alice", "bob"],
  "RolesAllowed": ["HR", "Manager"]
}
```

### Upload a Blob

```http
PUT /{container}/{blobPath}
Authorization: Bearer <token>
Content-Type: application/octet-stream
x-ms-meta-department: Engineering
x-ms-meta-access: public

<file content>
```

### Download a Blob

```http
GET /{container}/{blobPath}
Authorization: Bearer <token>
```

### Get Blob Metadata

```http
HEAD /{container}/{blobPath}
Authorization: Bearer <token>
```

### Update Metadata Only

```http
PUT /{container}/{blobPath}?comp=metadata
Authorization: Bearer <token>
x-ms-meta-department: HR
x-ms-meta-version: 2.0
```

## Access Control

### Authorization Headers

When uploading files, you can specify access control via metadata headers:

- `x-ms-meta-access: public` - Allow public read access
- `x-ms-meta-roles: Manager,HR` - Restrict to specific roles
- `x-ms-meta-users: alice,bob` - Restrict to specific users

### Access Rules

1. **Admin Role** - Full access to all containers and blobs
2. **Owner** - Full read/write access to owned blobs
3. **Public Access** - Anyone can read (if `access=public`)
4. **Role-Based** - Users with matching roles can read
5. **User-Based** - Explicitly allowed users can read

### Example Auth Scenarios

**Private file (owner only):**
```http
PUT /mycontainer/private.txt
x-ms-meta-access: private
```

**Public file (anyone can read):**
```http
PUT /mycontainer/public.txt
x-ms-meta-access: public
```

**Department-restricted file:**
```http
PUT /mycontainer/hr-doc.pdf
x-ms-meta-roles: HR,Manager
x-ms-meta-users: alice
```

## Project Structure

```
MiniBlob.Api/
├── Controllers/
│   ├── BlobController.cs          # Blob CRUD operations
│   └── ContainersController.cs    # Container management
├── Services/
│   ├── IStorageService.cs         # Storage abstraction
│   ├── FileSystemStorageService.cs # File system implementation
│   ├── FileMetadataAuthService.cs # Authorization logic
│   ├── ISearchIndex.cs            # Search abstraction
│   ├── EfSearchIndex.cs           # EF Core search implementation
│   └── NoOpSearchIndex.cs         # No-op search (disabled)
├── Data/
│   └── Models/                    # EF Core models
└── Models/                        # DTOs and domain models

MiniBlob.Tests/
├── BlobIntegrationTests.cs        # End-to-end API tests
├── FileMetadataAuthServiceTests.cs # Auth unit tests
├── ContainerControllerTests.cs    # Container tests
├── AuthInheritanceTests.cs        # Auth inheritance tests
└── MiniBlobPerfTests.cs           # Performance benchmarks
```

## Client Examples

### C# Client

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public class MiniBlobClient
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly string _token;

    public MiniBlobClient(string baseUrl, string jwtToken)
    {
        _baseUrl = baseUrl;
        _token = jwtToken;
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", jwtToken);
    }

    // Upload a file
    public async Task<bool> UploadFileAsync(string container, string blobPath, 
        string filePath, Dictionary<string, string>? metadata = null)
    {
        try
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var url = $"/{container}/{blobPath}";

            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new ByteArrayContent(fileBytes)
            };
            request.Content.Headers.ContentType = 
                new MediaTypeHeaderValue("application/octet-stream");

            // Add metadata headers
            if (metadata != null)
            {
                foreach (var kv in metadata)
                {
                    request.Headers.Add($"x-ms-meta-{kv.Key}", kv.Value);
                }
            }

            var response = await _client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Upload failed: {ex.Message}");
            return false;
        }
    }

    // Download a file
    public async Task<bool> DownloadFileAsync(string container, string blobPath, 
        string destinationPath)
    {
        try
        {
            var url = $"/{container}/{blobPath}";
            var response = await _client.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(destinationPath, content);
            
            // Read metadata from response headers
            foreach (var header in response.Headers)
            {
                if (header.Key.StartsWith("x-ms-meta-"))
                {
                    Console.WriteLine($"{header.Key}: {string.Join(",", header.Value)}");
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download failed: {ex.Message}");
            return false;
        }
    }
}

// Usage example
public class Program
{
    public static async Task Main()
    {
        var client = new MiniBlobClient(
            "https://localhost:7700", 
            "your-jwt-token-here"
        );

        // Upload
        var metadata = new Dictionary<string, string>
        {
            ["department"] = "Engineering",
            ["version"] = "1.0",
            ["access"] = "public"
        };
        
        await client.UploadFileAsync(
            "mycontainer", 
            "docs/report.pdf", 
            @"C:\files\report.pdf",
            metadata
        );

        // Download
        await client.DownloadFileAsync(
            "mycontainer",
            "docs/report.pdf",
            @"C:\downloads\report.pdf"
        );
    }
}
```

### JavaScript/TypeScript Client

```javascript
class MiniBlobClient {
    constructor(baseUrl, jwtToken) {
        this.baseUrl = baseUrl;
        this.token = jwtToken;
    }

    // Upload a file
    async uploadFile(container, blobPath, file, metadata = {}) {
        try {
            const url = `${this.baseUrl}/${container}/${blobPath}`;
            const headers = {
                'Authorization': `Bearer ${this.token}`,
                'Content-Type': 'application/octet-stream'
            };

            // Add metadata headers
            for (const [key, value] of Object.entries(metadata)) {
                headers[`x-ms-meta-${key}`] = value;
            }

            const response = await fetch(url, {
                method: 'PUT',
                headers: headers,
                body: file
            });

            return response.ok;
        } catch (error) {
            console.error('Upload failed:', error);
            return false;
        }
    }

    // Download a file
    async downloadFile(container, blobPath) {
        try {
            const url = `${this.baseUrl}/${container}/${blobPath}`;
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${this.token}`
                }
            });

            if (!response.ok) {
                throw new Error(`Download failed: ${response.status}`);
            }

            // Get metadata from response headers
            const metadata = {};
            for (const [key, value] of response.headers.entries()) {
                if (key.startsWith('x-ms-meta-')) {
                    metadata[key.replace('x-ms-meta-', '')] = value;
                }
            }
            console.log('Metadata:', metadata);

            // Return blob for download
            const blob = await response.blob();
            return blob;
        } catch (error) {
            console.error('Download failed:', error);
            return null;
        }
    }

    // Helper: Download and trigger browser download
    async downloadFileToUser(container, blobPath, filename) {
        const blob = await this.downloadFile(container, blobPath);
        if (!blob) return false;

        // Create download link
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        window.URL.revokeObjectURL(url);
        
        return true;
    }
}

// Usage example (Browser)
const client = new MiniBlobClient(
    'https://localhost:7700',
    'your-jwt-token-here'
);

// Upload from file input
document.getElementById('uploadBtn').addEventListener('click', async () => {
    const fileInput = document.getElementById('fileInput');
    const file = fileInput.files[0];
    
    if (file) {
        const metadata = {
            'department': 'Engineering',
            'version': '1.0',
            'access': 'public'
        };
        
        const success = await client.uploadFile(
            'mycontainer',
            `uploads/${file.name}`,
            file,
            metadata
        );
        
        console.log('Upload success:', success);
    }
});

// Download file
document.getElementById('downloadBtn').addEventListener('click', async () => {
    await client.downloadFileToUser(
        'mycontainer',
        'uploads/report.pdf',
        'report.pdf'
    );
});
```


## Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter BlobIntegrationTests

# Run performance tests (normally skipped)
dotnet test --filter FullyQualifiedName~MiniBlobPerfTests
```

## Performance

Benchmark results (1000 documents, 5-20MB each):

- **Sequential Upload**: ~XX MB/sec
- **Parallel Upload (8 workers)**: ~XX MB/sec

See `MiniBlobPerfTests.cs` and `AnotherPerfTest.cs` for detailed performance testing.

## Configuration Options

### Storage

Configure storage root path:

Linux:
```json
{
  "MiniBlob": {
    "RootPath": "/var/MiniBlobStorage"
  }
}
```
Windows
```json
{
  "MiniBlob": {
    "RootPath": "C:\\Temp\\MiniBlobStorage"
  }
}
```
### Search Indexing

**Important**: The built-in metadata indexing functionality is provided for reference purposes only and uses SQLite. It is **not recommended for production use**.

For production scenarios, you should:
- Never enable internal indexing; never set `MiniBlob::EnableSearchIndexing` to true
- Store file URLs and metadata in your application's database (PostgreSQL, SQL Server, MySQL, etc.)
- Implement search functionality in your calling system using a proper RDBMS
- Treat MiniBlob as a pure storage layer only

```csharp
// Reference implementation only (not for production)
services.AddScoped<ISearchIndex, EfSearchIndex>();
```

**Best Practice Architecture**:
```
Your Application (PostgreSQL/SQL Server)
  ├── Stores: File metadata, search indexes, business data
  ├── Stores: MiniBlob URLs (e.g., "/container1/docs/file.pdf")
  └── Calls: MiniBlob API for upload/download only

MiniBlob API
  └── Stores: Raw files only (no indexing)
```

## Security Considerations

- Always use HTTPS in production
- Store JWT secret keys securely (use environment variables or Azure Key Vault)
- Regularly rotate JWT signing keys
- Set appropriate file system permissions on the storage directory
- Consider implementing rate limiting for public endpoints
- Review and audit `.auth` files regularly


## License

This project is licensed under the MIT License - see the [LICENCE](LICENCE) file for details.

---

**Note**: This is a reference implementation for educational purposes. For production use, consider additional security hardening, monitoring, and backup strategies.