using Microsoft.AspNetCore.Mvc.Testing;
using MiniBlob.Api.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MiniBlob.Tests;

public class PerformanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const int SamplePoolSize = 15;
    private static readonly Random Rand = new();

    public PerformanceTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    private static async Task<List<string>> GenerateSampleFilesAsync() {
        var temp = Path.Combine(Path.GetTempPath(), "MiniBlobPerfPool");
        Directory.CreateDirectory(temp);

        var paths = new List<string>();
        var extensions = new[] { ".jpg", ".png", ".pdf", ".txt" };

        for (int i = 0; i < SamplePoolSize; i++) {
            var ext = extensions[Rand.Next(extensions.Length)];
            var filePath = Path.Combine(temp, $"sample_{i}{ext}");

            int sizeMb = Rand.Next(5, 21); // 5–20 MB
            int sizeBytes = sizeMb * 1024 * 1024;

            var bytes = new byte[sizeBytes];
            Rand.NextBytes(bytes);
            await File.WriteAllBytesAsync(filePath, bytes);

            paths.Add(filePath);
        }

        return paths;
    }
    
    // to run this specific test :
    // 1. remove Skip = "Performance test"; attribute should look like this: [Fact]
    // 2. dotnet test --filter "FullyQualifiedName=MiniBlob.Tests.PerformanceTests.MiniBlob_Performance_HttpSequential_1000Docs"
    //
    [Fact(Skip = "Performance test")]
    public async Task MiniBlob_Performance_HttpSequential_1000Docs() {
        var client = _factory.CreateClient();

        // Admin token
        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("tester", new[] { "admin" }, key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sampleFiles = await GenerateSampleFilesAsync();
        Assert.True(sampleFiles.Count >= 10);
        
        // MAKE SURE CONTAINER EXISTS

        // Ensure container exists
        //var createResp = await client.PostAsync("/container1", new StringContent(
        //    JsonSerializer.Serialize(new { UsersAllowed = new[] { "tester" }, RolesAllowed = new[] { "admin" } }),
        //    Encoding.UTF8, "application/json"));
        //createResp.EnsureSuccessStatusCode();

        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        for (int i = 0; i < 1000; i++) {
            var filePath = sampleFiles[i % sampleFiles.Count];
            var contentBytes = await File.ReadAllBytesAsync(filePath);
            totalBytes += contentBytes.Length;

            var id = Guid.NewGuid().ToString("N");
            var url = $"/container1/path/sub/{id}{Path.GetExtension(filePath)}";

            var req = new HttpRequestMessage(HttpMethod.Put, url) {
                Content = new ByteArrayContent(contentBytes)
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            if (i % 100 == 0)
                Console.WriteLine($"Uploaded {i}/1000");
        }

        sw.Stop();
        Console.WriteLine($"Sequential upload 1000 docs: {sw.Elapsed}");
        Console.WriteLine($"Throughput: {totalBytes / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds:F2} MB/sec");
    }

    [Fact(Skip = "Performance test")]
    public async Task MiniBlob_Performance_HttpParallel_1000Docs() {
        var client = _factory.CreateClient();

        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("tester", ["admin"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sampleFiles = await GenerateSampleFilesAsync();
        Assert.True(sampleFiles.Count >= 10);

        // Ensure container exists
        //var createResp = await client.PostAsync("/container1", new StringContent(
        //    JsonSerializer.Serialize(new { UsersAllowed = new[] { "tester" }, RolesAllowed = new[] { "admin" } }),
        //    Encoding.UTF8, "application/json"));
        //createResp.EnsureSuccessStatusCode();

        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        int parallelism = 8;
        var semaphore = new SemaphoreSlim(parallelism);

        var tasks = Enumerable.Range(0, 1000).Select(async i => {
            await semaphore.WaitAsync();
            try {
                var filePath = sampleFiles[i % sampleFiles.Count];
                var contentBytes = await File.ReadAllBytesAsync(filePath);
                Interlocked.Add(ref totalBytes, contentBytes.Length);

                var id = Guid.NewGuid().ToString("N");
                var url = $"/container1/path/sub/{id}{Path.GetExtension(filePath)}";

                var req = new HttpRequestMessage(HttpMethod.Put, url) {
                    Content = new ByteArrayContent(contentBytes)
                };
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var resp = await client.SendAsync(req);
                resp.EnsureSuccessStatusCode();

                if (i % 100 == 0)
                    Console.WriteLine($"Uploaded {i}/1000");
            }
            finally {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        sw.Stop();
        Console.WriteLine($"Parallel (8 workers) upload 1000 docs: {sw.Elapsed}");
        Console.WriteLine($"Throughput: {totalBytes / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds:F2} MB/sec");
    }

    //[Fact]
    [Fact(Skip = "Performance test")]
    public async Task MiniBlob_Performance_HttpParallel_200Docs() {
        var client = _factory.CreateClient();

        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken("tester", ["admin"], key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sampleFiles = await GenerateSampleFilesAsync();
        Assert.True(sampleFiles.Count >= 10);

        // Ensure container exists
        //var createResp = await client.PostAsync("/container1", new StringContent(
        //    JsonSerializer.Serialize(new { UsersAllowed = new[] { "tester" }, RolesAllowed = new[] { "admin" } }),
        //    Encoding.UTF8, "application/json"));
        //createResp.EnsureSuccessStatusCode();

        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        int parallelism = 8;
        var semaphore = new SemaphoreSlim(parallelism);

        var tasks = Enumerable.Range(0, 200).Select(async i => {
            await semaphore.WaitAsync();
            try {
                var filePath = sampleFiles[i % sampleFiles.Count];
                var contentBytes = await File.ReadAllBytesAsync(filePath);
                Interlocked.Add(ref totalBytes, contentBytes.Length);

                var id = Guid.NewGuid().ToString("N");
                var url = $"/container1/path/sub/{id}{Path.GetExtension(filePath)}";

                var req = new HttpRequestMessage(HttpMethod.Put, url) {
                    Content = new ByteArrayContent(contentBytes)
                };
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var resp = await client.SendAsync(req);
                resp.EnsureSuccessStatusCode();

                if (i % 100 == 0)
                    Console.WriteLine($"Uploaded {i}/200");
            }
            finally {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        sw.Stop();
        Console.WriteLine($"Parallel (8 workers) upload 200 docs: {sw.Elapsed}");
        Console.WriteLine($"Throughput: {totalBytes / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds:F2} MB/sec");
    }

}
