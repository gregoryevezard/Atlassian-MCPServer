using System.ComponentModel;
using Atlassian.McpServer.Services;
using ModelContextProtocol.Server;

namespace Atlassian.McpServer.Tools;

[McpServerResourceType]
public static class ConfluenceResources
{
    // ----------------------------
    // Ressources "directes"
    // ----------------------------

    [McpServerResource(
        UriTemplate = "confluence://spaces",
        MimeType = "application/json"),
     Description("List Confluence spaces (REST v2).")]
    public static async Task<string> SpacesAsync(
        ConfluenceV2Client client,
        CancellationToken ct)
    {
        var json = await client.ListSpacesAsync(keys: null, limit: 25, ct);
        return json.GetRawText();
    }

    // ----------------------------
    // Ressource template: root folders d’un space
    // ----------------------------

    [McpServerResource(
        UriTemplate = "confluence://root-folders/{spaceKey}",
        MimeType = "application/json"),
     Description("List ONLY root-level folders (type=folder) for a Confluence spaceKey (REST v2).")]
    public static async Task<string> RootFoldersAsync(
        ConfluenceV2Client client,
        string spaceKey,
        CancellationToken ct)
    {
        var json = await client.RootFoldersAsync(spaceKey, ct);
        return json.GetRawText();
    }

    // ----------------------------
    // Ressource template: lire une page
    // ----------------------------

    [McpServerResource(
        UriTemplate = "confluence://page/{pageId}",
        MimeType = "application/json"),
     Description("Get a Confluence page by id (REST v2), without children.")]
    public static async Task<string> PageAsync(
        ConfluenceV2Client client,
        string pageId,
        CancellationToken ct)
    {
        var json = await client.GetPageAsync(pageId, includeChildren: false, ct);
        return json.GetRawText();
    }

    // ----------------------------
    // Ressource template: lire un folder
    // ----------------------------

    [McpServerResource(
        UriTemplate = "confluence://folder/{folderId}",
        MimeType = "application/json"),
     Description("Get a Confluence folder by id (REST v2), without children.")]
    public static async Task<string> FolderAsync(
        ConfluenceV2Client client,
        string folderId,
        CancellationToken ct)
    {
        var json = await client.GetFolderAsync(folderId, includeChildren: false, ct);
        return json.GetRawText();
    }
}