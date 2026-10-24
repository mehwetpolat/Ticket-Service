using System.Globalization;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Services;

public sealed class SetupStatusService(
    ISettingsService settings,
    IProjectService projects,
    IMailConfigService mail,
    IMailSenderService senders,
    ILocalLlmClient llm,
    IMailIngestionRepository ingestions,
    ITicketRepository tickets,
    ITicketTargetCatalog catalog,
    IClock clock,
    AppRuntime runtime) : ISetupStatusService
{
    private const string OwnerId = "local-user";

    private sealed class TimestampSetting { public long At { get; set; } }
    private sealed class ProviderTestSetting
    {
        public long At { get; set; }
        public string Target { get; set; } = string.Empty;
        public string DestinationId { get; set; } = string.Empty;
        public bool DestinationChecked { get; set; }
        public string DestinationName { get; set; } = string.Empty;
    }
    private sealed class LegacyJiraTestSetting { public long At { get; set; } public bool ProjectChecked { get; set; } }

    public async Task<SetupStatusDto> GetAsync(string requestOrigin, CancellationToken ct = default)
    {
        var providersTask = settings.GetProvidersAsync(OwnerId, ct);
        var projectTask = projects.GetAsync(OwnerId, ct);
        var mailTask = mail.GetForAdminAsync(ct);
        var pollTask = mail.GetPollStatusAsync(ct);
        var sendersTask = senders.ListAsync(ct);
        var domainsTask = senders.ListDomainsAsync(ct);
        var passwordTask = settings.GetAsync<TimestampSetting>(OwnerId, "admin_password_changed_at", ct);
        var testTask = settings.GetAsync<ProviderTestSetting>(OwnerId, "ticket_provider_last_test", ct);
        var legacyTask = settings.GetAsync<LegacyJiraTestSetting>(OwnerId, "jira_last_test_at", ct);
        var llmTask = llm.CheckAsync(OwnerId, ct);
        var statsTask = ingestions.StatsAsync(ct);
        var publishedTask = tickets.CountPublishedAsync(OwnerId, ct);
        await Task.WhenAll(providersTask, projectTask, mailTask, pollTask, sendersTask, domainsTask, passwordTask, testTask, legacyTask, llmTask, statsTask, publishedTask);

        var providers = await providersTask;
        var project = await projectTask;
        var mailConfig = await mailTask;
        var pollStatus = await pollTask;
        var senderList = await sendersTask;
        var domainList = await domainsTask;
        var passwordChanged = await passwordTask;
        var ticketTest = await testTask;
        var legacyJiraTest = await legacyTask;
        var llmStatus = await llmTask;
        var stats = await statsTask;
        var publishedCount = await publishedTask;

        var definition = catalog.Get(project.Target);
        var ticketConfigured = catalog.ProviderConfigured(project.Target, providers);
        var targetTest = ticketTest is not null && ticketTest.At > 0 && ticketTest.Target == Key(project.Target) &&
            (project.DestinationId.Length == 0 || ticketTest.DestinationId == project.DestinationId) ? ticketTest : null;
        var legacyTest = project.Target == Domain.Enums.TicketTarget.Jira && legacyJiraTest is { At: > 0 } ? legacyJiraTest : null;
        var ticketTested = targetTest is not null || legacyTest is not null;
        var destinationTested = targetTest?.DestinationChecked == true || legacyTest?.ProjectChecked == true;
        var projectConfigured = project.DestinationId.Length > 0;
        var mailboxes = mailConfig?.Mailboxes ?? new List<AdminMailbox>();
        var mailConfigured = mailboxes.Count > 0 && mailboxes.All(box => box.HasPassword);
        var mailConnected = pollStatus?.State == MailCheckState.Connected && pollStatus.CheckedAt > 0;
        var sendersConfigured = senderList.Any(sender => sender.Active) || domainList.Count > 0;
        var dryRunSeen = stats.GetValueOrDefault("dry_run") > 0 || stats.GetValueOrDefault("published") > 0;
        var liveEnabled = mailConfig?.Enabled == true && mailConfig.DryRun == false;

        var steps = new List<SetupStepDto>
        {
            new("password", "Yönetici şifresini değiştir",
                "İlk kurulum şifresi yalnızca yönetici hesabını oluşturmak içindir. Kalıcı ve güçlü bir şifre belirleyin.",
                passwordChanged is { At: > 0 },
                passwordChanged is { At: > 0 }
                    ? $"Şifre {DateTimeOffset.FromUnixTimeSeconds(passwordChanged.At).ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("tr-TR"))} tarihinde değiştirildi."
                    : "İlk girişten sonra Şifre ve erişim bölümünden değiştirin. Ardından data/secrets/admin-password dosyasını silin.",
                "security"),
            new("integration", $"{definition.Label} bağlantısını kur",
                $"{definition.Label} için gereken API anahtarını/tokenı girin ve bağlantıyı test edin. Ayrıntılı yönergeler Entegrasyonlar bölümünde ve docs/ticket-systems klasöründedir.",
                ticketConfigured && ticketTested,
                ticketTested ? $"{definition.Label} kimlik doğrulaması başarılı." :
                    ticketConfigured ? "Bilgiler kayıtlı; Entegrasyonlar bölümünde Bağlantıyı test et düğmesine basın." :
                    $"{definition.Label} API bilgilerini Entegrasyonlar bölümünden girin.",
                "integrations"),
            new("destination", $"{definition.DestinationLabel} seç",
                definition.DestinationHelp,
                projectConfigured && destinationTested,
                !projectConfigured ? $"{definition.DestinationLabel} olmadan otomatik ticket açılamaz." :
                    destinationTested ? $"Hedef doğrulandı: {project.DestinationId}" :
                    $"Hedef kayıtlı: {project.DestinationId}. Bağlantı testini yeniden çalıştırarak hedefe erişimi doğrulayın.",
                "project"),
            new("model", "Yerel modeli hazırla",
                "Ollama kurulumla birlikte gelir. Qwen3 4B modelini indirin; e-postalar kurum ağından çıkmadan analiz edilir.",
                llmStatus.Connected && llmStatus.Installed,
                llmStatus.Connected
                    ? llmStatus.Installed ? $"{llmStatus.Model} hazır." : $"{llmStatus.Model} henüz indirilmemiş."
                    : "Model sunucusuna ulaşılamıyor; Docker servislerini kontrol edin.",
                "model"),
            new("mail", "IT mail kutusunu başla",
                "IT biriminin gelen kutusunun IMAP bilgilerini girin. Gerekirse birden fazla hesap ekleyin. Parola şifreli saklanır.",
                mailConfigured && mailConnected,
                mailboxes.Count == 0 ? "Henüz mail hesabı eklenmedi." :
                    mailConnected ? $"{mailboxes.Count} mail hesabı kontrol ediliyor." :
                    "Ayarları kaydedin; poller ilk kontrolde bağlantı durumunu burada gösterecek.",
                "mail"),
            new("senders", "İzinli gönderenleri belirle",
                "Kurum alan adınızı ekleyin (örn. sirket.com) veya tek tek gönderen adreslerini tanımlayın. Yalnızca izinli adresler ticket'a dönüşür.",
                sendersConfigured,
                sendersConfigured ? $"{senderList.Count(sender => sender.Active)} adres, {domainList.Count} alan adı izinli." :
                    "Kurum alan adını eklemek, tüm çalışanların taleplerini tek seferde kabul etmenin en kolay yoludur.",
                "mail"),
            new("test", "Deneme modunda bir e-posta işle",
                $"Önce Deneme modunda çalıştırın. IT kutusuna bir test e-postası gönderin ve Mail işlem geçmişinde dry_run kaydını görün; {definition.ShortLabel} tarafında kayıt oluşmaz.",
                dryRunSeen,
                dryRunSeen ? "En az bir e-posta işlendi." : "Deneme modunda işlenen e-postalar canlı moda geçince otomatik gönderilmez.",
                "mail"),
            new("live", "Otomatik ticket açmayı etkinleştir",
                $"Canlı moda geçin. Bundan sonra izinli gönderenlerden gelen uygun e-postalar arka planda otomatik olarak {definition.Label} kaydına dönüşür; arayüz açık olması gerekmez.",
                liveEnabled,
                liveEnabled ? "Mail kanalı canlı ve otomatik çalışıyor." : "Son adım: Mail sistemi > Gönderim modu > Canlı.",
                "mail")
        };

        var nextStep = steps.FirstOrDefault(step => !step.Complete)?.Id;
        return new SetupStatusDto(
            steps,
            nextStep is null,
            nextStep,
            new SetupCountsDto(
                stats.GetValueOrDefault("dry_run"), stats.GetValueOrDefault("published"),
                stats.GetValueOrDefault("failed"), stats.GetValueOrDefault("ignored"), publishedCount),
            new SetupMailDto(mailConfigured, mailConfig?.Enabled == true, mailConfig?.DryRun != false, mailboxes.Count, pollStatus),
            new SetupTicketSystemDto(project.Target, definition.Label, definition.ShortLabel, definition.DestinationLabel,
                definition.DestinationHelp, definition.DocsUrl, definition.ApiDocsUrl, ticketConfigured, ticketTested,
                project.DestinationId, destinationTested),
            new SetupLlmDto(llmStatus.Connected, llmStatus.Installed, llmStatus.Model, llmStatus.Endpoint),
            new SetupSendersDto(senderList.Count, domainList.Count),
            string.IsNullOrWhiteSpace(runtime.PublicOrigin) ? requestOrigin : runtime.PublicOrigin!,
            clock.UnixNow);
    }

    private static string Key(Domain.Enums.TicketTarget target) => target.ToString().ToLowerInvariant();
}
