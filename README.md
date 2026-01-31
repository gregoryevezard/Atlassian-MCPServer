# Atlassian MCP Server (.NET)

.NET **MCP server** (transport **stdio**) for **Confluence Cloud (REST v2)** and **Jira Cloud (REST v3)**.

## Features

- **Scoped API Token + Basic Auth** (email:token)
- Automatic **cloudId** resolution via `https://<site>/_edge/tenant_info`
- Confluence REST v2 via Atlassian gateway
- Jira REST v3 via Atlassian gateway

## Prerequisites

- .NET SDK **10.0+**
- An Atlassian Cloud site (e.g. `https://example.atlassian.net`)
- An API token with appropriate scopes for Confluence and/or Jira

## Configuration (appsettings.json)

Copy the example and fill in your values:

```bash
cp src/Atlassian.McpServer/appsettings.exemple.json src/Atlassian.McpServer/appsettings.json
```

Example:

```json
{
  "Atlassian": {
    "Site": "https://example.atlassian.net",
    "Email": "you@company.com",
    "ApiTokenConfluence": "your_confluence_token_scoped",
    "ApiTokenJira": "your_jira_token_scoped"
  }
}
```

Notes:
- **Never commit** `appsettings.json` (use `.gitignore`).
- If you only use one product (Confluence/Jira), leave the other token empty.

## Build & Run

```bash
dotnet restore src/Atlassian.McpServer/Atlassian.McpServer.csproj
dotnet build   src/Atlassian.McpServer/Atlassian.McpServer.csproj -c Release
dotnet run     --project src/Atlassian.McpServer/Atlassian.McpServer.csproj -c Release
```

The MCP process listens on **stdin/stdout**.

## Codex / VS Code Integration (DLL)

In Codex `config.toml`, add an MCP server pointing to the Release DLL:

```toml
[mcp_servers.atlassian]
command = "dotnet"
args = [
  "/path/to/repo/tools/atlassian-mcpserver/src/Atlassian.McpServer/bin/Release/net10.0/Atlassian.McpServer.dll"
]
```

Then restart VS Code (or the Codex extension) to reload the config.

## Exposed Tools

### Confluence

- `confluence_list_spaces(keys?, limit)`
- `confluence_get_space_homepage_id(space_key)`
- `confluence_get_space_info(space_key)`
- `confluence_get_folder(folder_id, include_children)`
- `confluence_get_page(page_id, include_children)`
- `confluence_root_folders(space_key)`
- `confluence_upsert_page(space_key, parent_id?, page_id?, title, body_storage_html, status?, version_message?, subtype?)`
  - **Recommended Jira reference**: insert a Jira macro in `body_storage_html`:
    ```xml
    <ac:structured-macro ac:name="jira">
      <ac:parameter ac:name="key">KEY</ac:parameter>
    </ac:structured-macro>
    ```

### Jira

- `jira_list_projects(query?, start_at, max_results, order_by?)`
- `jira_get_project(project_id_or_key)`
- `jira_get_issue(issue_id_or_key, fields_csv?, expand_csv?)`
- `jira_search_jql(jql, max_results, next_page_token?)`
- `jira_create_issue(project_key, issue_type, summary, description?, labels_csv?, parent_issue?)`
- `jira_delete_issue(issue_id_or_key, delete_subtasks)`

## Exposed Resources

- `confluence://spaces`
- `confluence://page/{pageId}`
- `confluence://folder/{folderId}`
- `confluence://root-folders/{spaceKey}`
- `jira://projects`
- `jira://project/{projectIdOrKey}`
- `jira://issue/{issueIdOrKey}`
- `jira://issue/createmeta/{projectKey}`
- `jira://issue/editmeta/{issueIdOrKey}`

## Notes

- Logs are sent to **stderr** to avoid polluting the JSON-RPC stream.
- The server uses `ModelContextProtocol` (C#). If needed:
  ```bash
  dotnet add package ModelContextProtocol --prerelease
  ```
