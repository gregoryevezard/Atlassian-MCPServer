using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atlassian.McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace Atlassian.McpServer.Services;

public sealed class ConfluenceV2Client
{
    private readonly IHttpClientFactory _http;
    private readonly AtlassianCloudIdResolver _resolver;
    private readonly AtlassianOptions _opts;

    public ConfluenceV2Client(
        IHttpClientFactory http,
        AtlassianCloudIdResolver resolver,
        IOptions<AtlassianOptions> options)
    {
        _http = http;
        _resolver = resolver;
        _opts = options.Value;
    }

    private static string ToBasic(string email, string token)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));

    private async Task<(HttpClient Client, string BaseUrl, string CloudId)> CreateClientAsync(CancellationToken ct)
    {
        var cloudId = await _resolver.ResolveCloudIdAsync(_opts.Site, _opts.Email, _opts.ApiTokenConfluence, ct);
        var baseUrl = $"https://api.atlassian.com/ex/confluence/{cloudId}/wiki/api/v2";

        var c = _http.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", ToBasic(_opts.Email, _opts.ApiTokenConfluence));
        c.DefaultRequestHeaders.Accept.Clear();
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        c.DefaultRequestHeaders.UserAgent.ParseAdd("confluence-mcpserver/1.0");

        return (c, baseUrl, cloudId);
    }

    // ---------------------------------------------------------------------
    // Spaces
    // ---------------------------------------------------------------------

    public async Task<JsonElement> ListSpacesAsync(string? keys, int limit, CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);

        if (limit <= 0) limit = 25;

        var url = $"{baseUrl}/spaces?limit={limit}";
        if (!string.IsNullOrWhiteSpace(keys))
            url += $"&keys={Uri.EscapeDataString(keys)}";

        var body = await client.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    public async Task<string> GetHomepageIdAsync(string spaceKey, CancellationToken ct)
    {
        var (_, homepageId) = await GetSpaceInfoAsync(spaceKey, ct);
        return homepageId;
    }

    public async Task<(string SpaceId, string HomepageId)> GetSpaceInfoAsync(string spaceKey, CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);
        return await GetSpaceInfoWithClientAsync(client, baseUrl, spaceKey, ct);
    }

    private static async Task<(string SpaceId, string HomepageId)> GetSpaceInfoWithClientAsync(
        HttpClient client,
        string baseUrl,
        string spaceKey,
        CancellationToken ct)
    {
        var url = $"{baseUrl}/spaces?keys={Uri.EscapeDataString(spaceKey)}&limit=1";
        var body = await client.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Space '{spaceKey}' not found or inaccessible.");
        }

        var space = results[0];

        var spaceId = space.GetProperty("id").GetString();
        var homepageId = space.GetProperty("homepageId").GetString();

        if (string.IsNullOrWhiteSpace(spaceId))
            throw new InvalidOperationException($"Space '{spaceKey}' missing id.");
        if (string.IsNullOrWhiteSpace(homepageId))
            throw new InvalidOperationException($"Space '{spaceKey}' missing homepageId.");

        return (spaceId!, homepageId!);
    }

    // ---------------------------------------------------------------------
    // Read content
    // ---------------------------------------------------------------------

    public async Task<JsonElement> GetFolderAsync(string folderId, bool includeChildren, CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);
        var url = $"{baseUrl}/folders/{folderId}" + (includeChildren ? "?include-direct-children=true" : "");

        var body = await client.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetPageAsync(string pageId, bool includeChildren, CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);
        var url = $"{baseUrl}/pages/{pageId}" + (includeChildren ? "?include-direct-children=true" : "");

        var body = await client.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Payload)> TryGetAsync(
        HttpClient client,
        string url,
        CancellationToken ct)
    {
        using var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

        return (resp.StatusCode, doc.RootElement.Clone());
    }

    // ---------------------------------------------------------------------
    // Root folders helper
    // ---------------------------------------------------------------------

    public async Task<JsonElement> RootFoldersAsync(string spaceKey, CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);

        // Single call to /spaces using the same client
        var (_, homepageId) = await GetSpaceInfoWithClientAsync(client, baseUrl, spaceKey, ct);

        var q = "?include-direct-children=true";

        // 1) try folder(homepageId)
        var (statusFolder, payloadFolder) = await TryGetAsync(client, $"{baseUrl}/folders/{homepageId}{q}", ct);

        JsonElement rootPayload;
        if (statusFolder == HttpStatusCode.OK)
        {
            rootPayload = payloadFolder;
        }
        else if (statusFolder == HttpStatusCode.NotFound)
        {
            // 2) fallback page(homepageId)
            var (statusPage, payloadPage) = await TryGetAsync(client, $"{baseUrl}/pages/{homepageId}{q}", ct);
            if (statusPage != HttpStatusCode.OK)
                throw new InvalidOperationException(
                    $"Root homepageId={homepageId} not found as folder (404). Page returned {(int)statusPage}: {payloadPage}");

            rootPayload = payloadPage;
        }
        else
        {
            throw new InvalidOperationException($"Root container fetch failed HTTP {(int)statusFolder}: {payloadFolder}");
        }

        if (!rootPayload.TryGetProperty("directChildren", out var dc) || dc.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Unexpected payload: missing 'directChildren'.");

        if (!dc.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected payload: missing 'directChildren.results' array.");

        using var outDoc = BuildRootFoldersResponse(spaceKey, homepageId, rootPayload, dc, results);
        return outDoc.RootElement.Clone();
    }

    private static JsonDocument BuildRootFoldersResponse(
        string spaceKey,
        string homepageId,
        JsonElement rootPayload,
        JsonElement directChildren,
        JsonElement resultsArray)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();

            w.WritePropertyName("results");
            w.WriteStartArray();
            foreach (var item in resultsArray.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
                    item.WriteTo(w);
            }
            w.WriteEndArray();

            if (directChildren.TryGetProperty("meta", out var meta))
            {
                w.WritePropertyName("meta");
                meta.WriteTo(w);
            }

            if (directChildren.TryGetProperty("_links", out var links))
            {
                w.WritePropertyName("_links");
                links.WriteTo(w);
            }

            w.WritePropertyName("source");
            w.WriteStartObject();
            w.WriteString("spaceKey", spaceKey);
            w.WriteString("homepageId", homepageId);
            w.WriteString("rootType", rootPayload.TryGetProperty("type", out var rt) ? rt.GetString() : "unknown");
            w.WriteEndObject();

            w.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray());
    }

    // ---------------------------------------------------------------------
    // Create / Update page (v2)
    // ---------------------------------------------------------------------

    public async Task<JsonElement> CreatePageAsync(
        string spaceId,
        string parentId,
        string title,
        string bodyStorageHtml,
        string status,
        string? subtype,
        CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);

        var payload = new
        {
            spaceId,
            status = string.IsNullOrWhiteSpace(status) ? "current" : status,
            title,
            parentId,
            body = new { representation = "storage", value = bodyStorageHtml },
            subtype = string.IsNullOrWhiteSpace(subtype) ? null : subtype
        };

        using var resp = await client.PostAsJsonAsync($"{baseUrl}/pages", payload, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Create page failed HTTP {(int)resp.StatusCode}: {respBody}");

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(respBody) ? "{}" : respBody);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> UpdatePageAsync(
        string pageId,
        string title,
        string bodyStorageHtml,
        string status,
        string? versionMessage,
        CancellationToken ct)
    {
        var (client, baseUrl, _) = await CreateClientAsync(ct);

        // GET /pages/{id} to retrieve current version.number
        var currentJson = await client.GetStringAsync($"{baseUrl}/pages/{pageId}", ct);
        using var currentDoc = JsonDocument.Parse(currentJson);
        var current = currentDoc.RootElement;

        if (!current.TryGetProperty("version", out var ver) || !ver.TryGetProperty("number", out var numEl))
            throw new InvalidOperationException("Update page failed: missing version.number on GET /pages/{id}");

        var nextVersion = numEl.GetInt32() + 1;

        var payload = new
        {
            id = pageId,
            status = string.IsNullOrWhiteSpace(status) ? "current" : status,
            title,
            body = new { representation = "storage", value = bodyStorageHtml },
            version = new
            {
                number = nextVersion,
                message = versionMessage ?? ""
            }
        };

        using var resp = await client.PutAsJsonAsync($"{baseUrl}/pages/{pageId}", payload, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Update page failed HTTP {(int)resp.StatusCode}: {respBody}");

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(respBody) ? "{}" : respBody);
        return doc.RootElement.Clone();
    }
}