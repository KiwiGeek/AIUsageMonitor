using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIUsageMonitor.Services;

public static class ClaudeOAuthCredentialsService
{
    private const string TokenRefreshEndpoint = "https://console.anthropic.com/v1/oauth/token";
    private const string OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly string[] OAuthPropertyNames = ["claudeAiOauth", "oauthAccount"];

    public static string GetDefaultCredentialsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", ".credentials.json");
    }

    public static async Task<AccessTokenResult> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var credentialsPath = GetDefaultCredentialsPath();
        if (!File.Exists(credentialsPath))
        {
            return AccessTokenResult.Missing(credentialsPath);
        }

        var account = TryReadAccount(credentialsPath);
        if (account is null)
        {
            return AccessTokenResult.Missing(credentialsPath);
        }

        if (!IsExpired(account))
        {
            return AccessTokenResult.Success(account.AccessToken, credentialsPath);
        }

        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return AccessTokenResult.Failed(
                credentialsPath,
                "Claude OAuth access token is expired and no refresh token is available. Run claude auth login.");
        }

        return await RefreshAccessTokenAsync(credentialsPath, account, cancellationToken);
    }

    private static bool IsExpired(OAuthAccount account)
    {
        if (account.ExpiresAtMs is null)
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(account.ExpiresAtMs.Value);
        return expiresAt <= DateTimeOffset.UtcNow.Add(ExpiryBuffer);
    }

    private static async Task<AccessTokenResult> RefreshAccessTokenAsync(
        string credentialsPath,
        OAuthAccount account,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenRefreshEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    grant_type = "refresh_token",
                    refresh_token = account.RefreshToken,
                    client_id = OAuthClientId
                }),
                Encoding.UTF8,
                "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AccessTokenResult.Failed(
                credentialsPath,
                $"Claude OAuth token refresh failed: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return AccessTokenResult.Failed(
                    credentialsPath,
                    $"Claude OAuth token refresh returned {(int)response.StatusCode} ({response.ReasonPhrase}). Run claude auth login.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!root.TryGetProperty("access_token", out var accessTokenElement))
            {
                return AccessTokenResult.Failed(
                    credentialsPath,
                    "Claude OAuth token refresh response did not include an access token.");
            }

            var newAccessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                return AccessTokenResult.Failed(
                    credentialsPath,
                    "Claude OAuth token refresh returned an empty access token.");
            }

            var newRefreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
                ? refreshTokenElement.GetString()
                : null;

            long? newExpiresAtMs = null;
            if (root.TryGetProperty("expires_in", out var expiresInElement) &&
                expiresInElement.TryGetInt64(out var expiresInSeconds))
            {
                newExpiresAtMs = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds).ToUnixTimeMilliseconds();
            }
            else if (root.TryGetProperty("expires_at", out var expiresAtElement) &&
                     expiresAtElement.TryGetInt64(out var expiresAtMs))
            {
                newExpiresAtMs = expiresAtMs;
            }

            try
            {
                await UpdateCredentialsAsync(
                    credentialsPath,
                    account.OAuthPropertyName,
                    newAccessToken,
                    newRefreshToken ?? account.RefreshToken,
                    newExpiresAtMs,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return AccessTokenResult.Failed(
                    credentialsPath,
                    $"Claude OAuth token refresh succeeded but could not update {credentialsPath}: {ex.Message}");
            }

            return AccessTokenResult.Success(newAccessToken, credentialsPath);
        }
    }

    private static async Task UpdateCredentialsAsync(
        string credentialsPath,
        string oauthPropertyName,
        string newAccessToken,
        string? newRefreshToken,
        long? newExpiresAtMs,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(credentialsPath, cancellationToken);
        var backupPath = credentialsPath + ".backup";
        await File.WriteAllTextAsync(backupPath, text, cancellationToken);

        var node = JsonNode.Parse(text) ?? throw new JsonException("Credentials file was empty.");
        if (node[oauthPropertyName] is not JsonObject oauth)
        {
            throw new JsonException($"Credentials file is missing {oauthPropertyName}.");
        }

        oauth["accessToken"] = newAccessToken;
        if (!string.IsNullOrWhiteSpace(newRefreshToken))
        {
            oauth["refreshToken"] = newRefreshToken;
        }

        if (newExpiresAtMs.HasValue)
        {
            oauth["expiresAt"] = newExpiresAtMs.Value;
        }

        await File.WriteAllTextAsync(
            credentialsPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static OAuthAccount? TryReadAccount(string credentialsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath));
            var root = document.RootElement;

            foreach (var oauthPropertyName in OAuthPropertyNames)
            {
                if (!root.TryGetProperty(oauthPropertyName, out var oauth))
                {
                    continue;
                }

                if (!oauth.TryGetProperty("accessToken", out var tokenElement))
                {
                    continue;
                }

                var accessToken = tokenElement.GetString();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    continue;
                }

                var refreshToken = oauth.TryGetProperty("refreshToken", out var refreshElement)
                    ? refreshElement.GetString()
                    : null;
                long? expiresAtMs = oauth.TryGetProperty("expiresAt", out var expiresElement) &&
                                     expiresElement.TryGetInt64(out var expiresMs)
                    ? expiresMs
                    : null;

                return new OAuthAccount(accessToken, refreshToken, expiresAtMs, oauthPropertyName);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private sealed record OAuthAccount(
        string AccessToken,
        string? RefreshToken,
        long? ExpiresAtMs,
        string OAuthPropertyName);

    public sealed record AccessTokenResult(
        bool Succeeded,
        string? AccessToken,
        string CredentialsPath,
        string? ErrorMessage)
    {
        public static AccessTokenResult Success(string accessToken, string credentialsPath) =>
            new(true, accessToken, credentialsPath, null);

        public static AccessTokenResult Missing(string credentialsPath) =>
            new(false, null, credentialsPath, null);

        public static AccessTokenResult Failed(string credentialsPath, string message) =>
            new(false, null, credentialsPath, message);
    }
}
