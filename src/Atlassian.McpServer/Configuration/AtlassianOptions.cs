namespace Atlassian.McpServer.Configuration;

public sealed class AtlassianOptions
{
    /// <summary>
    /// Base site URL like: https://gregoryevezard.atlassian.net
    /// Used to resolve cloudId via /_edge/tenant_info
    /// </summary>
    public string Site { get; set; } = "";

    /// <summary>
    /// Atlassian account email for basic auth
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// Scoped API token for Confluence (may differ from Jira token)
    /// </summary>
    public string ApiTokenConfluence { get; set; } = "";

    /// <summary>
    /// Scoped API token for Jira (may differ from Confluence token)
    /// </summary>
    public string ApiTokenJira { get; set; } = "";
}