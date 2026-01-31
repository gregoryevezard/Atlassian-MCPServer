using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Atlassian.McpServer.Tools;

[McpServerToolType]
public sealed class ConfluenceTools
{
    private readonly Services.ConfluenceV2Client _client;

    public ConfluenceTools(Services.ConfluenceV2Client client) => _client = client;

    [McpServerTool, Description("List Confluence spaces (REST v2). Optionally filter by comma-separated keys.")]
    public async Task<JsonElement> confluence_list_spaces(
        [Description("Comma-separated space keys (optional), e.g. ABC,HR")] string? keys,
        [Description("Max number of spaces to return (default 25)")] int limit,
        CancellationToken ct)
        => await _client.ListSpacesAsync(keys, limit <= 0 ? 25 : limit, ct);

    [McpServerTool, Description("Get the homepageId for a Confluence space key (REST v2).")]
    public async Task<string> confluence_get_space_homepage_id(
        [Description("Confluence space key, e.g. ABC")] string space_key,
        CancellationToken ct)
        => await _client.GetHomepageIdAsync(space_key, ct);

    [McpServerTool, Description("Get space info for a Confluence space key (REST v2): spaceId + homepageId.")]
    public async Task<JsonElement> confluence_get_space_info(
        [Description("Confluence space key, e.g. ABC")] string space_key,
        CancellationToken ct)
    {
        var (spaceId, homepageId) = await _client.GetSpaceInfoAsync(space_key, ct);

        // return JsonElement for consistent tool outputs
        using var doc = JsonDocument.Parse($$"""
        {
          "spaceKey": "{{space_key}}",
          "spaceId": "{{spaceId}}",
          "homepageId": "{{homepageId}}"
        }
        """);
        return doc.RootElement.Clone();
    }

    [McpServerTool, Description("Get a Confluence folder by id (REST v2).")]
    public async Task<JsonElement> confluence_get_folder(
        [Description("Folder id")] string folder_id,
        [Description("Include direct children")] bool include_children,
        CancellationToken ct)
        => await _client.GetFolderAsync(folder_id, include_children, ct);

    [McpServerTool, Description("Get a Confluence page by id (REST v2).")]
    public async Task<JsonElement> confluence_get_page(
        [Description("Page id")] string page_id,
        [Description("Include direct children")] bool include_children,
        CancellationToken ct)
        => await _client.GetPageAsync(page_id, include_children, ct);

    [McpServerTool, Description("List ONLY root-level folders (type=folder) for a Confluence space. Handles folder/page homepage fallback.")]
    public async Task<JsonElement> confluence_root_folders(
        [Description("Confluence space key, e.g. ABC")] string space_key,
        CancellationToken ct)
        => await _client.RootFoldersAsync(space_key, ct);

    [McpServerTool, Description(
        "Create or update a Confluence page (REST v2). " +
        "If page_id is empty => create under parent_id (or homepageId if parent_id is empty). " +
        "If page_id is provided => update that page (auto increments version.number). " +
        "To reference a Jira issue in body_storage_html, use the Jira macro: " +
        "<ac:structured-macro ac:name=\"jira\"><ac:parameter ac:name=\"key\">KEY</ac:parameter></ac:structured-macro>.")]
    public async Task<JsonElement> confluence_upsert_page(
        [Description("Confluence space key, e.g. ABC")] string space_key,
        [Description("Parent id for CREATE only. If empty, uses the space homepageId.")] string? parent_id,
        [Description("Page id to UPDATE. If empty => CREATE.")] string? page_id,
        [Description("Page title")] string title,
        [Description("Body in Confluence 'storage' representation (HTML).")] string body_storage_html,
        [Description("Status: current or draft (default current).")] string? status,
        [Description("Optional version message for UPDATE (stored in version.message).")] string? version_message,
        [Description("Optional subtype for CREATE (leave null unless needed).")] string? subtype,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(space_key))
            throw new ArgumentException("space_key is required.", nameof(space_key));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(body_storage_html))
            throw new ArgumentException("body_storage_html is required.", nameof(body_storage_html));

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "current" : status.Trim();

        var (spaceId, homepageId) = await _client.GetSpaceInfoAsync(space_key.Trim(), ct);

        if (string.IsNullOrWhiteSpace(page_id))
        {
            // CREATE
            var parent = string.IsNullOrWhiteSpace(parent_id) ? homepageId : parent_id.Trim();

            var created = await _client.CreatePageAsync(
                spaceId: spaceId,
                parentId: parent,
                title: title.Trim(),
                bodyStorageHtml: body_storage_html,
                status: normalizedStatus,
                subtype: string.IsNullOrWhiteSpace(subtype) ? null : subtype.Trim(),
                ct: ct);

            using var doc = JsonDocument.Parse($$"""
            {
              "action": "created",
              "spaceKey": "{{space_key.Trim()}}",
              "spaceId": "{{spaceId}}",
              "parentId": "{{parent}}"
            }
            """);

            // Merge "page" object into response (preserve Confluence JSON)
            // We'll build a new JsonDocument with page embedded.
            var response = BuildUpsertResponse("created", space_key.Trim(), spaceId, parent, null, created);
            return response.RootElement.Clone();
        }
        else
        {
            // UPDATE
            var updated = await _client.UpdatePageAsync(
                pageId: page_id.Trim(),
                title: title.Trim(),
                bodyStorageHtml: body_storage_html,
                status: normalizedStatus,
                versionMessage: version_message,
                ct: ct);

            var response = BuildUpsertResponse("updated", space_key.Trim(), spaceId, null, page_id.Trim(), updated);
            return response.RootElement.Clone();
        }
    }

    private static JsonDocument BuildUpsertResponse(
        string action,
        string spaceKey,
        string spaceId,
        string? parentId,
        string? pageId,
        JsonElement page)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();

            w.WriteString("action", action);
            w.WriteString("spaceKey", spaceKey);
            w.WriteString("spaceId", spaceId);

            if (!string.IsNullOrWhiteSpace(parentId))
                w.WriteString("parentId", parentId);

            if (!string.IsNullOrWhiteSpace(pageId))
                w.WriteString("pageId", pageId);

            w.WritePropertyName("page");
            page.WriteTo(w);

            w.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray());
    }
}
