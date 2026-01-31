using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Atlassian.McpServer.Services;

public sealed class AtlassianCloudIdResolver
{
    private readonly IHttpClientFactory _http;
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    public AtlassianCloudIdResolver(IHttpClientFactory http) => _http = http;

    public async Task<string> ResolveCloudIdAsync(string siteBaseUrl, string email, string apiToken, CancellationToken ct)
    {
        // Cache key should include site + email + token hash-ish (don’t log token)
        var cacheKey = $"{siteBaseUrl.TrimEnd('/')}|{email}|{StableTokenKey(apiToken)}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url = siteBaseUrl.TrimEnd('/') + "/_edge/tenant_info";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", ToBasic(email, apiToken));
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _http.CreateClient();
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"cloudId resolve failed HTTP {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("cloudId", out var cloudIdProp))
            throw new InvalidOperationException($"cloudId not found in tenant_info response: {body}");

        var cloudId = cloudIdProp.GetString();
        if (string.IsNullOrWhiteSpace(cloudId))
            throw new InvalidOperationException("cloudId is null/empty");

        _cache[cacheKey] = cloudId!;
        return cloudId!;
    }

    private static string ToBasic(string email, string token)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));

    // Avoid storing raw token in cache key
    private static string StableTokenKey(string token)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).Substring(0, 12);
}