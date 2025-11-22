namespace MiniBlob.Tests;

using global::MiniBlob.Api.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniBlob.Api.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

public class ContainerControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ContainerControllerTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    private static HttpClient CreateClientWithToken(WebApplicationFactory<Program> factory, string userName, string[] roles) {
        var client = factory.CreateClient();
        var key = "abcdefghijklmnopqrstuvwx12345678";
        var token = TokenHelper.GenerateToken(userName, roles, key, "mini-blob", "mini-blob-audience", 60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task AdminCanCreateContainer_CreatesContainerAuthFile() {
        // Arrange
        var client = CreateClientWithToken(_factory, "admin", [ "admin" ]);

        // Use the factory to create a service scope
        await using var scope = _factory.Services.CreateAsyncScope();
        
        // Get the IConfiguration service from the scope
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var storageRoot = configuration["MiniBlob:RootPath"] ?? "MiniBlobStorage";

        var containerName = "container1";

        // Prepare POST body
        var payload = new {
            UsersAllowed = new[] { "alice", "bob" },
            RolesAllowed = new[] { "HR", "Manager" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync($"/containers/{containerName}", content);

        // Assert
        response.EnsureSuccessStatusCode();

        // Check that .container.auth exists
        var containerPath = Path.Combine(storageRoot, containerName);
        var authPath = Path.Combine(containerPath, ".container.auth");
        Assert.True(File.Exists(authPath), ".container.auth should exist after container creation");

        // Validate contents
        var json = await File.ReadAllTextAsync(authPath);
        var authInfo = JsonSerializer.Deserialize<FileMetadataAuthService.AuthInfo>(json);
        Assert.NotNull(authInfo);
        Assert.Contains("admin", authInfo!.RolesAllowed);
        Assert.Contains("alice", authInfo.UsersAllowed);
        Assert.Contains("bob", authInfo.UsersAllowed);
    }


    [Fact]
    public async Task NonAdminCannotCreateContainer_ReturnsForbid() {
        var client = CreateClientWithToken(_factory, "john", new[] { "HR" });
        var containerName = "unauthorized-container";
        var payload = new { AllowedUsers = Array.Empty<string>(), AllowedRoles = Array.Empty<string>() };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/containers/{containerName}/create", content);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);

        var authPath = Path.Combine("Storage", containerName, ".container.auth");
        Assert.False(File.Exists(authPath));
    }
}
