using System.ComponentModel;
using Atlassian.McpServer.Services;
using ModelContextProtocol.Server;

namespace Atlassian.McpServer.Tools;

[McpServerResourceType]
public static class JiraResources
{
    [McpServerResource(
        UriTemplate = "jira://projects",
        MimeType = "application/json"),
     Description("List Jira projects (REST v3).")]
    public static async Task<string> ProjectsAsync(
        JiraV2Client client,
        CancellationToken ct)
    {
        var json = await client.ListProjectsAsync(query: null, startAt: 0, maxResults: 50, orderBy: null, ct);
        return json.GetRawText();
    }

    [McpServerResource(
        UriTemplate = "jira://project/{projectIdOrKey}",
        MimeType = "application/json"),
     Description("Get a Jira project by id or key (REST v3).")]
    public static async Task<string> ProjectAsync(
        JiraV2Client client,
        string projectIdOrKey,
        CancellationToken ct)
    {
        var json = await client.GetProjectAsync(projectIdOrKey, ct);
        return json.GetRawText();
    }

    [McpServerResource(
        UriTemplate = "jira://issue/{issueIdOrKey}",
        MimeType = "application/json"),
     Description("Get a Jira issue by id or key (REST v3).")]
    public static async Task<string> IssueAsync(
        JiraV2Client client,
        string issueIdOrKey,
        CancellationToken ct)
    {
        var json = await client.GetIssueAsync(issueIdOrKey, fieldsCsv: null, expandCsv: null, ct);
        return json.GetRawText();
    }
}