# Atlassian MCP Server (.NET)

Serveur **MCP .NET** (transport **stdio**) pour **Confluence Cloud (REST v2)** et **Jira Cloud (REST v3)**.

## Fonctionnalités

- Authentification **API Token + Basic Auth** (email:token)
- Résolution automatique du **cloudId** via `https://<site>/_edge/tenant_info`
- Confluence REST v2 via la gateway Atlassian
- Jira REST v3 via la gateway Atlassian

## Prérequis

- .NET SDK **8.0+**
- Un site Atlassian Cloud (ex: `https://example.atlassian.net`)
- Un token API avec scopes adaptés pour Confluence et/ou Jira

## Configuration (appsettings.json)

Copier l’exemple puis renseigner vos valeurs :

```bash
cp src/Atlassian.McpServer/appsettings.exemple.json src/Atlassian.McpServer/appsettings.json
```

Exemple :

```json
{
  "Atlassian": {
    "Site": "https://example.atlassian.net",
    "Email": "you@company.com",
    "ApiTokenConfluence": "your_confluence_token",
    "ApiTokenJira": "your_jira_token"
  }
}
```

Notes :
- **Ne commitez jamais** `appsettings.json` (utilisez `.gitignore`).
- Si vous n’utilisez qu’un produit (Confluence/Jira), laissez l’autre token vide.

## Build & Run

```bash
dotnet restore src/Atlassian.McpServer/Atlassian.McpServer.csproj
dotnet build   src/Atlassian.McpServer/Atlassian.McpServer.csproj -c Release
dotnet run     --project src/Atlassian.McpServer/Atlassian.McpServer.csproj -c Release
```

Le process MCP écoute sur **stdin/stdout**.

## Intégration Codex / VS Code (DLL)

Dans le fichier `config.toml` de Codex, ajoutez un serveur MCP pointant sur la DLL Release :

```toml
[mcp_servers.atlassian]
command = "dotnet"
args = [
  "/path/to/repo/tools/atlassian-mcpserver/src/Atlassian.McpServer/bin/Release/net10.0/Atlassian.McpServer.dll"
]
```

Puis redémarrez VS Code (ou l’extension Codex) pour recharger la config.

## Tools exposés

### Confluence

- `confluence_list_spaces(keys?, limit)`
- `confluence_get_space_homepage_id(space_key)`
- `confluence_get_space_info(space_key)`
- `confluence_get_folder(folder_id, include_children)`
- `confluence_get_page(page_id, include_children)`
- `confluence_root_folders(space_key)`
- `confluence_upsert_page(space_key, parent_id?, page_id?, title, body_storage_html, status?, version_message?, subtype?)`
  - **Référence Jira recommandée** : insérer un macro Jira dans `body_storage_html` :
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

## Resources exposées

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

- Les logs sont envoyés sur **stderr** pour ne pas polluer le flux JSON-RPC.
- Le serveur utilise `ModelContextProtocol` (C#). Si besoin :
  ```bash
  dotnet add package ModelContextProtocol --prerelease
  ```
