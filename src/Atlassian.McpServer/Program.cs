using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Atlassian.McpServer.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Atlassian.McpServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();

        // Log to stderr (recommended for stdio MCP servers)
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Information);

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<Services.AtlassianCloudIdResolver>();
        builder.Services.AddSingleton<Services.ConfluenceV2Client>();
        builder.Services.AddSingleton<Services.JiraV2Client>();

        
        builder.Services
            .AddOptions<AtlassianOptions>()
            .Bind(builder.Configuration.GetSection("Atlassian"))
            .Validate(o =>
                !string.IsNullOrWhiteSpace(o.Site) &&
                !string.IsNullOrWhiteSpace(o.Email) &&
                !string.IsNullOrWhiteSpace(o.ApiTokenConfluence) &&
                !string.IsNullOrWhiteSpace(o.ApiTokenJira),
                "Atlassian configuration is incomplete")
            .ValidateOnStart();

        // MCP server over stdio + auto-discover tools from this assembly
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly();

        await builder.Build().RunAsync();
    }
}
