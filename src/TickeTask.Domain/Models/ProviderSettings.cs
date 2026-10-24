using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Models;

public class ProviderSettings
{
    public JiraProviderSettings Jira { get; set; } = new();
    public TrelloProviderSettings Trello { get; set; } = new();
    public GithubProviderSettings Github { get; set; } = new();
    public AsanaProviderSettings Asana { get; set; } = new();
    public ClickupProviderSettings Clickup { get; set; } = new();

    public static ProviderSettings Empty() => new();
}

public class JiraProviderSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class TrelloProviderSettings
{
    public string Key { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class GithubProviderSettings
{
    public string Token { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public class AsanaProviderSettings
{
    public string Token { get; set; } = string.Empty;
}

public class ClickupProviderSettings
{
    public string Token { get; set; } = string.Empty;
}

public class LlmSettings
{
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "qwen3:4b";
}

public class ProjectConfig
{
    public TicketTarget Target { get; set; } = TicketTarget.Jira;
    public string DestinationId { get; set; } = string.Empty;
    public string Name { get; set; } = "Kurum içi BT";
}
