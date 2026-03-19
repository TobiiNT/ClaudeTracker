using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClaudeTracker.Models;
using ClaudeTracker.Services;
using ClaudeTracker.Services.Interfaces;
using Moq;
using Moq.Protected;
using Xunit;

namespace ClaudeTracker.Tests;

public class OAuthTokenRefreshTests
{
    private static string MakeCredentialsJson(string accessToken = "old-token", string refreshToken = "refresh-123", long? expiresAt = null)
    {
        var creds = new CliCredentialsJson
        {
            ClaudeOAuth = new ClaudeOAuthData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                Scopes = new List<string> { "user:inference" },
                SubscriptionType = "pro"
            },
            OrganizationUuid = "org-uuid-123"
        };
        return JsonSerializer.Serialize(creds);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, string? responseBody = null)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(statusCode);
        if (responseBody != null)
            response.Content = new StringContent(responseBody, Encoding.UTF8, "application/json");

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return mockHandler;
    }

    [Fact]
    public async Task TryRefreshTokenAsync_WithValidRefreshToken_ReturnsTrue()
    {
        var mockCredentials = new Mock<ICredentialService>();
        var credJson = MakeCredentialsJson();
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(credJson);

        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 3600
        });

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, responseJson);
        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.True(result);
        mockCredentials.Verify(c => c.WriteCliCredentials(It.Is<string>(s => s.Contains("new-access-token"))), Times.Once);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NoRefreshToken_ReturnsFalse()
    {
        var mockCredentials = new Mock<ICredentialService>();
        var credJson = MakeCredentialsJson(refreshToken: "");
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(credJson);

        var mockHandler = CreateMockHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_ServerReturns401_ReturnsFalse()
    {
        var mockCredentials = new Mock<ICredentialService>();
        var credJson = MakeCredentialsJson();
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(credJson);

        var mockHandler = CreateMockHandler(HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NoCredentialsFile_ReturnsFalse()
    {
        var mockCredentials = new Mock<ICredentialService>();
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns((string?)null);

        var mockHandler = CreateMockHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_NetworkError_ReturnsFalse()
    {
        var mockCredentials = new Mock<ICredentialService>();
        var credJson = MakeCredentialsJson();
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(credJson);

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
        mockCredentials.Verify(c => c.WriteCliCredentials(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryRefreshTokenAsync_ResponseMissingAccessToken_ReturnsFalse()
    {
        var mockCredentials = new Mock<ICredentialService>();
        var credJson = MakeCredentialsJson();
        mockCredentials.Setup(c => c.ReadCliCredentials()).Returns(credJson);

        var responseJson = JsonSerializer.Serialize(new
        {
            refresh_token = "new-refresh",
            expires_in = 3600
        });

        var mockHandler = CreateMockHandler(HttpStatusCode.OK, responseJson);
        var httpClient = new HttpClient(mockHandler.Object);

        var service = new ClaudeCodeSyncService(mockCredentials.Object, httpClient);
        var result = await service.TryRefreshTokenAsync();

        Assert.False(result);
        mockCredentials.Verify(c => c.WriteCliCredentials(It.IsAny<string>()), Times.Never);
    }
}
