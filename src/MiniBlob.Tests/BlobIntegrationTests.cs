using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniBlob.Api.Helpers;
using MiniBlob.Api.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MiniBlob.Tests;

public class BlobIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BlobIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PutGetHeadMetadataTest()
    {
        var client = _factory.CreateClient();

        var key = "abcdefghijklmnopqrstuvwx12345678"; // 32 chars
        var token = TokenHelper.GenerateToken("tester", [ "admin" ], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = "/container1/path/sub/testfile.txt";
        var contentBytes = Encoding.UTF8.GetBytes("Hello EF-style");
        var req = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new ByteArrayContent(contentBytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        req.Headers.Add("x-ms-meta-department", "HR");
        req.Headers.Add("x-ms-meta-version", "2021-08-06");
        req.Headers.Add("x-ms-meta-order-id", "O1234556");
        req.Headers.Add("x-ms-meta-claim-id", "C3434545");
        var putResp = await client.SendAsync(req);
        putResp.EnsureSuccessStatusCode();

        var headResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        headResp.EnsureSuccessStatusCode();
        Assert.True(headResp.Headers.Contains("x-ms-meta-department"));

        var getResp = await client.GetAsync(url);
        getResp.EnsureSuccessStatusCode();
        var body = await getResp.Content.ReadAsStringAsync();
        Assert.Equal("Hello EF-style", body);
    }

    [Fact]
    public async Task GetDeniedForNonAdmin_WhenNoPropFile() {
        var client = _factory.CreateClient();

        // Non-admin user token
        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("user1", ["user"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = "/container1/privatefile.txt";

        // Simulate existing blob with no .prop file
        var putReq = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new StringContent("secret")
        };
        await client.SendAsync(putReq);

        // Try to GET as non-admin — expect forbidden
        var getResp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);
    }
    [Fact]
    public async Task OnlyAdminCanReadBlob_WhenNoPropFileExists() {
        var client = _factory.CreateClient();
        var key = "abcdefghijklmnopqrstuvwx12345678";

        var url = "/container1/private/testfile.txt";
        var contentBytes = Encoding.UTF8.GetBytes("Top secret data");
        var req = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new ByteArrayContent(contentBytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        // Upload the blob as admin (so it exists but with no .prop file)
        var adminToken = TokenHelper.GenerateToken("adminuser", ["admin"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var putResp = await client.SendAsync(req);
        putResp.EnsureSuccessStatusCode();

        // Delete the .prop file manually to simulate missing metadata
        await using var scope = _factory.Services.CreateAsyncScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
        
        var filePath = storageService.FileSystemPath("container1", "private/testfile.txt");
        var propFilePath = filePath + ".prop";
        if (File.Exists(propFilePath))
            File.Delete(propFilePath);

        //
        // 1 Try as regular user — expect 403 Forbidden
        //
        var userToken = TokenHelper.GenerateToken("user1", ["user"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var forbiddenResp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        //
        // 2. Try as admin — expect 200 OK and correct content
        //
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var okResp = await client.GetAsync(url);
        okResp.EnsureSuccessStatusCode();
        var body = await okResp.Content.ReadAsStringAsync();
        Assert.Equal("Top secret data", body);
    }

    [Fact]
    public async Task AuthFileCreatedOnUpload() {
        var client = _factory.CreateClient();
        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("tester", ["HR"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = "/container1/path/sub/testfile-auth.txt";
        var contentBytes = Encoding.UTF8.GetBytes("Auth test content");
        var req = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new ByteArrayContent(contentBytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        // Upload the blob
        var putResp = await client.SendAsync(req);
        putResp.EnsureSuccessStatusCode();

        // Check that the .auth file exists
        await using var scope = _factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        
        var filePath = storage.FileSystemPath("container1", "path/sub/testfile-auth.txt");
        var authFilePath = filePath + ".auth";
        
        //Make sure no auth file is created since container level security is used
        Assert.False(File.Exists(authFilePath),"Using container level security");

    }
    [Fact]
    public async Task PutCreatesFileAuth_WhenSpecialHeadersPresent() {
        var client = _factory.CreateClient();

        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("tester", ["HR"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = "/container1/path/file-special-auth.txt";
        var contentBytes = Encoding.UTF8.GetBytes("Test content");

        var req = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new ByteArrayContent(contentBytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        // Add headers that trigger file-specific auth creation
        req.Headers.Add("x-ms-meta-public", "true");
        req.Headers.Add("x-ms-meta-roles", "Manager,HR");
        req.Headers.Add("x-ms-meta-users", "alice,bob");

        // Upload the blob
        var putResp = await client.SendAsync(req);
        putResp.EnsureSuccessStatusCode();

        // Check that the .auth file exists
        await using var scope = _factory.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var filePath = storage.FileSystemPath("container1", "path/file-special-auth.txt");
        var authFilePath = filePath + ".auth";

        Assert.True(File.Exists(authFilePath), ".auth file should exist after upload");

        // Validate the contents
        var authJson = await File.ReadAllTextAsync(authFilePath);
        var authInfo = JsonSerializer.Deserialize<FileMetadataAuthService.AuthInfo>(authJson);

        Assert.NotNull(authInfo);
        Assert.Equal("tester", authInfo!.Owner);
        Assert.Contains("tester", authInfo.UsersAllowed);
        Assert.Contains("alice", authInfo.UsersAllowed);
        Assert.Contains("bob", authInfo.UsersAllowed);
        Assert.Contains("Manager", authInfo.RolesAllowed);
        Assert.Contains("HR", authInfo.RolesAllowed);
        Assert.Contains("public", authInfo.RolesAllowed); // because x-ms-meta-public=true
    }

}
