using System.Text.RegularExpressions;
using TickeTask.Application.Abstractions;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Tickets;

public partial class TicketTargetCatalog : ITicketTargetCatalog
{
    private static readonly IReadOnlyList<TicketTarget> AllTargets = new[]
    {
        TicketTarget.Jira, TicketTarget.Trello, TicketTarget.Github, TicketTarget.Asana, TicketTarget.Clickup
    };

    private static readonly IReadOnlyDictionary<TicketTarget, TargetDefinition> Definitions = new Dictionary<TicketTarget, TargetDefinition>
    {
        [TicketTarget.Jira] = new(TicketTarget.Jira, "jira", "Jira Cloud", "Jira", "Jira'da oluştur",
            "Jira proje anahtarı", "IT",
            "Jira'da projeyi açın; adres çubuğunda /projects/IT görünüyorsa anahtar IT'dir. Kayıt projenin backlog bölümüne düşer.",
            "https://id.atlassian.com/manage-profile/security/api-tokens",
            "https://developer.atlassian.com/cloud/jira/platform/rest/v3/",
            "Jira proje anahtarı 2-30 karakter olmalı, büyük harfle başlamalı ve yalnızca A-Z, 0-9, _ içermelidir (örn. IT, HELPDESK).", true),
        [TicketTarget.Trello] = new(TicketTarget.Trello, "trello", "Trello", "Trello", "Trello'da oluştur",
            "Trello liste ID", "5f1a2b3c4d5e6f7890abcdef",
            "Kartın açılacağı listenin 24 karakterlik kimliği. Board adresinin sonuna .json ekleyip lists[].id alanından veya Trello API'sinden alabilirsiniz.",
            "https://trello.com/app-key",
            "https://developer.atlassian.com/cloud/trello/rest/api-group-cards/#api-cards-post",
            "Trello liste ID'si 24 karakterlik hexadecimal olmalıdır (örn. 5f1a2b3c4d5e6f7890abcdef).", false),
        [TicketTarget.Github] = new(TicketTarget.Github, "github", "GitHub Issues", "GitHub", "GitHub'da oluştur",
            "GitHub deposu", "owner/repo",
            "Issue'nun açılacağı depo owner/repo biçiminde yazılır. Örn. https://github.com/acme/destek için acme/destek. Depoda Issues sekmesi açık olmalıdır.",
            "https://github.com/settings/tokens?type=beta",
            "https://docs.github.com/en/rest/issues/issues#create-an-issue",
            "GitHub deposu owner/repo biçiminde olmalıdır (örn. acme/destek).", false),
        [TicketTarget.Asana] = new(TicketTarget.Asana, "asana", "Asana", "Asana", "Asana'da oluştur",
            "Asana proje ID", "1201234567890123",
            "Görevin ekleneceği Asana projesinin sayısal GID'si. Proje adresindeki /0/<proje-GID>/ bölümünden alınır.",
            "https://developers.asana.com/docs/authentication",
            "https://developers.asana.com/reference/createtask",
            "Asana proje ID'si sayısal olmalıdır (örn. 1201234567890123).", false),
        [TicketTarget.Clickup] = new(TicketTarget.Clickup, "clickup", "ClickUp", "ClickUp", "ClickUp'ta oluştur",
            "ClickUp liste ID", "901234567",
            "Görevin açılacağı ClickUp listesinin kimliği. Liste adresindeki /li/<liste-ID> bölümünden alınır.",
            "https://app.clickup.com/settings/apps",
            "https://developer.clickup.com/reference/createtask",
            "ClickUp liste ID'si sayısal olmalıdır (örn. 901234567).", false)
    };

    public IReadOnlyList<TicketTarget> Targets => AllTargets;

    public bool IsTarget(string value) =>
        AllTargets.Any(target => Definitions[target].Key.Equals(value, StringComparison.OrdinalIgnoreCase));

    public TargetDefinition Get(TicketTarget target) => Definitions[target];

    public TargetDefinition Get(string target) =>
        IsTarget(target) ? Definitions.First(pair => pair.Value.Key.Equals(target, StringComparison.OrdinalIgnoreCase)).Value : Definitions[TicketTarget.Jira];

    public string NormalizeDestination(TicketTarget target, string value)
    {
        var destination = (value ?? string.Empty).Trim();
        if (target == TicketTarget.Github)
        {
            var match = GithubUrlRegex().Match(destination);
            if (match.Success) destination = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
            destination = Regex.Replace(destination, "\\.git$", string.Empty, RegexOptions.IgnoreCase).TrimEnd('/');
        }
        else if (target == TicketTarget.Jira) destination = destination.ToUpperInvariant();
        else if (target == TicketTarget.Trello) destination = destination.ToLowerInvariant();
        return destination;
    }

    public string? DestinationError(TicketTarget target, string value)
    {
        var definition = Get(target);
        if (string.IsNullOrEmpty(value)) return $"{definition.DestinationLabel} gerekli.";
        var normalized = NormalizeDestination(target, value);
        var valid = target switch
        {
            TicketTarget.Jira => JiraKeyRegex().IsMatch(normalized),
            TicketTarget.Trello => TrelloIdRegex().IsMatch(normalized),
            TicketTarget.Github => GithubRepoRegex().IsMatch(normalized),
            _ => NumericIdRegex().IsMatch(normalized)
        };
        return valid ? null : definition.PatternError;
    }

    public bool ProviderConfigured(TicketTarget target, ProviderSettings providers) => target switch
    {
        TicketTarget.Jira => !string.IsNullOrWhiteSpace(providers.Jira.BaseUrl) && !string.IsNullOrWhiteSpace(providers.Jira.Email) && !string.IsNullOrWhiteSpace(providers.Jira.Token),
        TicketTarget.Trello => !string.IsNullOrWhiteSpace(providers.Trello.Key) && !string.IsNullOrWhiteSpace(providers.Trello.Token),
        TicketTarget.Github => !string.IsNullOrWhiteSpace(providers.Github.Token),
        TicketTarget.Asana => !string.IsNullOrWhiteSpace(providers.Asana.Token),
        TicketTarget.Clickup => !string.IsNullOrWhiteSpace(providers.Clickup.Token),
        _ => false
    };

    [GeneratedRegex("^(?:https?:\\/\\/)?(?:www\\.)?github\\.com\\/([^/\\s]+)\\/([^/\\s#?]+)(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GithubUrlRegex();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,29}$")]
    private static partial Regex JiraKeyRegex();

    [GeneratedRegex("^[a-f0-9]{24}$")]
    private static partial Regex TrelloIdRegex();

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex GithubRepoRegex();

    [GeneratedRegex("^\\d{5,}$")]
    private static partial Regex NumericIdRegex();
}
