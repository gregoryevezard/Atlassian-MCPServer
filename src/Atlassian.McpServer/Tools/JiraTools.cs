using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Atlassian.McpServer.Tools;

[McpServerToolType]
public sealed class JiraTools
{
    private readonly Services.JiraV2Client _client;

    public JiraTools(Services.JiraV2Client client) => _client = client;

    [McpServerTool, Description("List Jira projects (REST v3). Supports optional query + pagination.")]
    public async Task<JsonElement> jira_list_projects(
        [Description("Optional search string (filters projects by name/key).")] string? query,
        [Description("Start index (default 0).")] int start_at,
        [Description("Max results (default 50, max 200).")] int max_results,
        [Description("Optional orderBy (e.g. 'key' or 'name').")] string? order_by,
        CancellationToken ct)
        => await _client.ListProjectsAsync(
            query: string.IsNullOrWhiteSpace(query) ? null : query,
            startAt: start_at < 0 ? 0 : start_at,
            maxResults: max_results <= 0 ? 50 : max_results,
            orderBy: string.IsNullOrWhiteSpace(order_by) ? null : order_by,
            ct: ct);

    [McpServerTool, Description("Get a Jira project by id or key (REST v3).")]
    public async Task<JsonElement> jira_get_project(
        [Description("Project id or key, e.g. 10000 or ABC")] string project_id_or_key,
        CancellationToken ct)
        => await _client.GetProjectAsync(project_id_or_key, ct);

    [McpServerTool, Description("Get a Jira issue by id or key (REST v3).")]
    public async Task<JsonElement> jira_get_issue(
        [Description("Issue id or key, e.g. 10001 or ABC-1")] string issue_id_or_key,
        [Description("Optional fields CSV, e.g. 'summary,status,assignee'")] string? fields_csv,
        [Description("Optional expand CSV, e.g. 'renderedFields,changelog'")] string? expand_csv,
        CancellationToken ct)
        => await _client.GetIssueAsync(
            issueIdOrKey: issue_id_or_key,
            fieldsCsv: string.IsNullOrWhiteSpace(fields_csv) ? null : fields_csv,
            expandCsv: string.IsNullOrWhiteSpace(expand_csv) ? null : expand_csv,
            ct: ct);

    [McpServerTool, Description("Create a Jira issue (REST v3 POST /issue). Description is plain text converted to ADF. Returns {id,key,self}.")]
    public async Task<JsonElement> jira_create_issue(
        [Description("Project key, e.g. ABC")] string project_key,
        [Description("Issue type name, e.g. Task, Story, Epic")] string issue_type,
        [Description("Summary/title of the issue")] string summary,
        [Description("Optional description (plain text). Will be converted to ADF.")] string? description,
        [Description("Optional labels CSV, e.g. 'codex,backend,urgent'")] string? labels_csv,
        [Description("Optional parent issue id or key (for sub-tasks), e.g. ABC-1")] string? parent_issue,
        CancellationToken ct)
    {
        try
        {
            var labels = string.IsNullOrWhiteSpace(labels_csv)
                ? null
                : labels_csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return await _client.CreateIssueAsync(
                projectKey: project_key,
                issueTypeName: issue_type,
                summary: summary,
                descriptionText: string.IsNullOrWhiteSpace(description) ? null : description,
                labels: labels,
                parentIssueKeyOrId: string.IsNullOrWhiteSpace(parent_issue) ? null : parent_issue,
                ct: ct);
        }
        catch (Exception ex)
        {
            // Force une erreur explicite côté Codex au lieu du générique "An error occurred..."
            throw new InvalidOperationException($"jira_create_issue failed: {ex.Message}", ex);
        }
    }
    
    [McpServerTool, Description("Delete a Jira issue (REST v3 DELETE /issue/{issueIdOrKey}). Optionally delete subtasks.")]
    public async Task<JsonElement> jira_delete_issue(
        [Description("Issue id or key, e.g. 10001 or ABC-1")] string issue_id_or_key,
        [Description("If true, delete subtasks as well (deleteSubtasks query param).")] bool delete_subtasks,
        CancellationToken ct)
        => await _client.DeleteIssueAsync(issue_id_or_key, delete_subtasks, ct);

    [McpServerTool, Description("Search Jira issues using POST /rest/api/3/search/jql (scoped token). Pagination uses nextPageToken only.")]
    public async Task<JsonElement> jira_search_jql(
        [Description("JQL query, e.g. 'project = ABC ORDER BY created DESC'")] string jql,
        [Description("Max results (default 50, max 200).")] int max_results,
        [Description("Pagination token from a previous response (optional).")] string? next_page_token,
        CancellationToken ct)
        => await _client.SearchJqlAsync(
            jql: jql,
            maxResults: max_results <= 0 ? 50 : max_results,
            nextPageToken: string.IsNullOrWhiteSpace(next_page_token) ? null : next_page_token,
            ct: ct);
}
