namespace MiniBlob.Tests;

using MiniBlob.Api.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;



public class SampleFileFixture : IAsyncLifetime
{
    public List<string> Files { get; private set; } = new();
    private static readonly Random Rand = new();
    public const int SamplePoolSize = 12;

    public async Task InitializeAsync() {
        Files = await GenerateSampleFilesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<List<string>> GenerateSampleFilesAsync() {
        var temp = Path.Combine(Path.GetTempPath(), "MiniBlobPerfPool_HTTP");
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
}

// -----------------------------------------------------------------------------
// Performance Tests
// -----------------------------------------------------------------------------

public class PerformanceHttpTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;
    private readonly HttpClient client;
    private const int TotalDocs = 1000;
    private readonly Random rand = new();

    public PerformanceHttpTests(SampleFileFixture fixture) {
        _fixture = fixture;

        client = new HttpClient { BaseAddress = new Uri("https://localhost:7700") };

        // -----------------------------------
        // Add Authorization header like real tests
        // -----------------------------------
        var key = "abcdefghijklmnopqrstuvwx12345678"; // 32 chars
        var token = TokenHelper.GenerateToken(
            "tester",
            ["admin" ],
            key,
            issuer: "mini-blob",
            audience: "mini-blob-audience",
            expireMinutes: 60
        );

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    // NOTE: you  need to start MiniBlob.Api locally with HTTPS on port 7700
    // before running these performance tests.
    //
    // SEQUENTIAL UPLOAD BENCHMARK. 
    // 1. Remove 'Skip = "Performance test - run manually"'; attribute should look like this: [Fact]
    // 2. dotnet test --filter "FullyQualifiedName=MiniBlob.Tests.PerformanceHttpTests.MiniBlob_Performance_HttpSequential_1000Docs"
    [Fact(Skip = "Performance test - run manually")] 
    public async Task MiniBlob_Performance_HttpSequential_1000Docs() {
        var files = _fixture.Files;

        long totalBytes = 0;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalDocs; i++) {
            var filePath = files[i % files.Count];
            var bytes = await File.ReadAllBytesAsync(filePath);
            totalBytes += bytes.Length;

            var id = Guid.NewGuid().ToString("N");
            var url = $"/container1/perf/{id}";

            var req = new HttpRequestMessage(HttpMethod.Put, url) {
                Content = new ByteArrayContent(bytes)
            };

            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            if (i % 100 == 0)
                Console.WriteLine($"Sequential upload: {i}/{TotalDocs}");
        }

        sw.Stop();
        PrintStats("Sequential HTTP PUT", sw.Elapsed, totalBytes);
    }

    // -------------------------------------------------------------------------
    // PARALLEL UPLOAD BENCHMARK (8 workers)
    // -------------------------------------------------------------------------
    [Fact()] //Skip = "Performance test – run manually"
    public async Task MiniBlob_Performance_HttpParallel_1000Docs() {
        var files = _fixture.Files;

        long totalBytes = 0;
        var sw = Stopwatch.StartNew();

        const int workers = 8;
        var tasks = new List<Task>();
        int counter = 0;
        var lockObj = new object();

        for (int w = 0; w < workers; w++) {
            tasks.Add(Task.Run(async () => {
                while (true) {
                    int i;
                    lock (lockObj) {
                        if (counter >= TotalDocs) break;
                        i = counter++;
                    }

                    var filePath = files[i % files.Count];
                    var bytes = await File.ReadAllBytesAsync(filePath);

                    lock (lockObj)
                        totalBytes += bytes.Length;

                    var id = Guid.NewGuid().ToString("N");
                    var url = $"/container1/perf/{id}";

                    var req = new HttpRequestMessage(HttpMethod.Put, url) {
                        Content = new ByteArrayContent(bytes)
                    };

                    var resp = await client.SendAsync(req);
                    resp.EnsureSuccessStatusCode();

                    if (i % 100 == 0)
                        Console.WriteLine($"Parallel upload: {i}/{TotalDocs}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        sw.Stop();
        PrintStats("Parallel HTTP PUT (8 workers)", sw.Elapsed, totalBytes);
    }

    // -------------------------------------------------------------------------
    // Stats printer
    // -------------------------------------------------------------------------
    private static void PrintStats(string label, TimeSpan elapsed, long totalBytes) {
        double totalMB = totalBytes / (1024.0 * 1024.0);
        double mbPerSec = totalMB / elapsed.TotalSeconds;

        Console.WriteLine("=====================================================");
        Console.WriteLine($"{label} Performance Summary");
        Console.WriteLine("-----------------------------------------------------");
        Console.WriteLine($"Total files:        {TotalDocs}");
        Console.WriteLine($"Total data:         {totalMB:F2} MB");
        Console.WriteLine($"Total time:         {elapsed}");
        Console.WriteLine($"Avg per document:   {elapsed.TotalMilliseconds / TotalDocs:F2} ms");
        Console.WriteLine($"Throughput:         {mbPerSec:F2} MB/sec");
        Console.WriteLine("=====================================================");
    }
}
