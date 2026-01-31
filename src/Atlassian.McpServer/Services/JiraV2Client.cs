using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Atlassian.McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace Atlassian.McpServer.Services;

public sealed class JiraV2Client
{
    private readonly IHttpClientFactory _http;
    private readonly AtlassianCloudIdResolver _resolver;
    private readonly AtlassianOptions _opts;

    public JiraV2Client(
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

    private static JsonElement ParseJson(string body)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return doc.RootElement.Clone();
    }

    private static void Log(string msg) => Console.Error.WriteLine(msg);

    private async Task<(HttpClient Client, string BaseUrl)> CreateClientAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.Site) ||
            string.IsNullOrWhiteSpace(_opts.Email) ||
            string.IsNullOrWhiteSpace(_opts.ApiTokenJira))
            throw new InvalidOperationException("AtlassianOptions incomplete for Jira (Site/Email/ApiTokenJira).");

        var cloudId = await _resolver.ResolveCloudIdAsync(_opts.Site, _opts.Email, _opts.ApiTokenJira, ct);

        // Jira Cloud gateway base
        var baseUrl = $"https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3";

        var c = _http.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", ToBasic(_opts.Email, _opts.ApiTokenJira));
        c.DefaultRequestHeaders.Accept.Clear();
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        c.DefaultRequestHeaders.UserAgent.ParseAdd("atlassian-mcpserver/1.0");

        return (c, baseUrl);
    }

    // ------------------------
    // Projects
    // ------------------------

    public async Task<JsonElement> ListProjectsAsync(
        string? query,
        int startAt,
        int maxResults,
        string? orderBy,
        CancellationToken ct)
    {
        var (client, baseUrl) = await CreateClientAsync(ct);

        if (startAt < 0) startAt = 0;
        if (maxResults <= 0) maxResults = 50;
        if (maxResults > 200) maxResults = 200;

        var url = $"{baseUrl}/project/search?startAt={startAt}&maxResults={maxResults}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(orderBy))
            url += $"&orderBy={Uri.EscapeDataString(orderBy)}";

        Log($"[JIRA] GET {url}");

        using var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"ListProjects failed HTTP {(int)resp.StatusCode}: {body}");

        return ParseJson(body);
    }

    public async Task<JsonElement> GetProjectAsync(string projectIdOrKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectIdOrKey))
            throw new InvalidOperationException("projectIdOrKey is required.");

        var (client, baseUrl) = await CreateClientAsync(ct);
        var url = $"{baseUrl}/project/{Uri.EscapeDataString(projectIdOrKey)}";

        Log($"[JIRA] GET {url}");

        using var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GetProject failed HTTP {(int)resp.StatusCode}: {body}");

        return ParseJson(body);
    }

    // ------------------------
    // Issues
    // ------------------------

    public async Task<JsonElement> GetIssueAsync(
        string issueIdOrKey,
        string? fieldsCsv,
        string? expandCsv,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issueIdOrKey))
            throw new InvalidOperationException("issueIdOrKey is required.");

        var (client, baseUrl) = await CreateClientAsync(ct);

        var url = $"{baseUrl}/issue/{Uri.EscapeDataString(issueIdOrKey)}";
        var qs = new List<string>();

        if (!string.IsNullOrWhiteSpace(fieldsCsv))
            qs.Add("fields=" + Uri.EscapeDataString(fieldsCsv));
        if (!string.IsNullOrWhiteSpace(expandCsv))
            qs.Add("expand=" + Uri.EscapeDataString(expandCsv));

        if (qs.Count > 0)
            url += "?" + string.Join("&", qs);

        Log($"[JIRA] GET {url}");

        using var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GetIssue failed HTTP {(int)resp.StatusCode}: {body}");

        return ParseJson(body);
    }

    public async Task<JsonElement> CreateIssueAsync(
        string projectKey,
        string issueTypeName,
        string summary,
        string? descriptionText,
        IEnumerable<string>? labels,
        string? parentIssueKeyOrId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            throw new InvalidOperationException("projectKey is required.");
        if (string.IsNullOrWhiteSpace(issueTypeName))
            throw new InvalidOperationException("issueTypeName is required.");
        if (string.IsNullOrWhiteSpace(summary))
            throw new InvalidOperationException("summary is required.");

        var (client, baseUrl) = await CreateClientAsync(ct);
        var url = $"{baseUrl}/issue"; // REST v3 create issue  [oai_citation:3‡Atlassian Developer](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issues/?utm_source=chatgpt.com)

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();

            w.WritePropertyName("fields");
            w.WriteStartObject();

            // project
            w.WritePropertyName("project");
            w.WriteStartObject();
            w.WriteString("key", projectKey);
            w.WriteEndObject();

            // issuetype
            w.WritePropertyName("issuetype");
            w.WriteStartObject();
            w.WriteString("name", issueTypeName);
            w.WriteEndObject();

            // summary
            w.WriteString("summary", summary);

            // description (ADF) – required for textarea fields in Jira Cloud  [oai_citation:4‡Atlassian Developer](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issues/?utm_source=chatgpt.com)
            if (!string.IsNullOrWhiteSpace(descriptionText))
            {
                w.WritePropertyName("description");
                WriteAdfParagraph(w, descriptionText);
            }

            // labels
            if (labels != null)
            {
                var arr = labels.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (arr.Length > 0)
                {
                    w.WritePropertyName("labels");
                    w.WriteStartArray();
                    foreach (var l in arr) w.WriteStringValue(l);
                    w.WriteEndArray();
                }
            }

            // parent (sub-task)
            if (!string.IsNullOrWhiteSpace(parentIssueKeyOrId))
            {
                w.WritePropertyName("parent");
                w.WriteStartObject();
                // Jira accepte "key" ou "id" selon ce que tu passes
                // Ici on met "key" si ça ressemble à "ABC-123", sinon "id"
                if (parentIssueKeyOrId.Contains('-', StringComparison.Ordinal))
                    w.WriteString("key", parentIssueKeyOrId);
                else
                    w.WriteString("id", parentIssueKeyOrId);

                w.WriteEndObject();
            }

            w.WriteEndObject(); // fields

            w.WriteEndObject(); // root
        }

        var payload = Encoding.UTF8.GetString(ms.ToArray());
        Console.Error.WriteLine($"[JIRA] POST {url}");
        Console.Error.WriteLine($"[JIRA] payload: {payload}");

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"CreateIssue failed HTTP {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return doc.RootElement.Clone();
    }

    private static void WriteAdfParagraph(Utf8JsonWriter w, string text)
    {
        // Minimal Atlassian Document Format
        w.WriteStartObject();
        w.WriteString("type", "doc");
        w.WriteNumber("version", 1);

        w.WritePropertyName("content");
        w.WriteStartArray();

        w.WriteStartObject();
        w.WriteString("type", "paragraph");
        w.WritePropertyName("content");
        w.WriteStartArray();

        w.WriteStartObject();
        w.WriteString("type", "text");
        w.WriteString("text", text ?? "");
        w.WriteEndObject(); // text

        w.WriteEndArray();  // paragraph.content
        w.WriteEndObject(); // paragraph

        w.WriteEndArray();  // doc.content
        w.WriteEndObject(); // doc
    }

    public async Task<JsonElement> DeleteIssueAsync(
        string issueIdOrKey,
        bool deleteSubtasks,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issueIdOrKey))
            throw new InvalidOperationException("issueIdOrKey is required.");

        var (client, baseUrl) = await CreateClientAsync(ct);

        var url = $"{baseUrl}/issue/{Uri.EscapeDataString(issueIdOrKey)}?deleteSubtasks={(deleteSubtasks ? "true" : "false")}";

        Console.Error.WriteLine($"[JIRA] DELETE {url}");

        using var resp = await client.DeleteAsync(url, ct);

        // Jira DELETE retourne souvent 204 No Content
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DeleteIssue failed HTTP {(int)resp.StatusCode}: {body}");

        // Retour MCP friendly (JSON)
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("deleted", issueIdOrKey);
            w.WriteBoolean("deleteSubtasks", deleteSubtasks);
            w.WriteNumber("httpStatus", (int)resp.StatusCode);
            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    // ------------------------
    // Search (NEW)
    // ------------------------

    public async Task<JsonElement> SearchJqlAsync(
        string jql,
        int maxResults,
        string? nextPageToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jql))
            throw new InvalidOperationException("jql is required.");

        var (client, baseUrl) = await CreateClientAsync(ct);

        if (maxResults <= 0) maxResults = 50;
        if (maxResults > 200) maxResults = 200;

        var url = $"{baseUrl}/search/jql";
        var payload = BuildSearchPayload(jql, maxResults, nextPageToken);

        Log($"[JIRA] POST {url}");
        Log($"[JIRA] payload: {payload}");

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"SearchJql failed HTTP {(int)resp.StatusCode}: {body}");

        return ParseJson(body);
    }

    private static string BuildSearchPayload(string jql, int maxResults, string? nextPageToken)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("jql", jql);
            w.WriteNumber("maxResults", maxResults);

            // Request all fields, need to implement later a param to specify fields
            w.WritePropertyName("fields");
            w.WriteStartArray();
            w.WriteStringValue("*all");
            w.WriteEndArray();


            // Pagination uses nextPageToken only (no startAt)
            if (!string.IsNullOrWhiteSpace(nextPageToken))
                w.WriteString("nextPageToken", nextPageToken);

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}