using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static readonly object AnalysisSchema = new
    {
        type = "object",
        required = new[] { "title", "description", "type", "priority", "subTasks" },
        properties = new
        {
            title = new { type = "string" },
            description = new { type = "string" },
            type = new { type = "string", @enum = new[] { "Hata", "Talep", "Yeni özellik", "Değişiklik" } },
            priority = new { type = "string", @enum = new[] { "critical", "high", "normal" } },
            dueDate = new { type = "string" },
            labels = new { type = "array", items = new { type = "string" } },
            confidence = new { type = "number" },
            questions = new { type = "array", items = new { type = "string" } },
            subTasks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    required = new[] { "title", "description" },
                    properties = new { title = new { type = "string" }, description = new { type = "string" } }
                }
            }
        }
    };

    private sealed class AnalyzedTicket
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? Priority { get; set; }
        public string? DueDate { get; set; }
        public List<string>? Labels { get; set; }
        public double? Confidence { get; set; }
        public List<string>? Questions { get; set; }
        public List<AnalyzedSubtask>? SubTasks { get; set; }
    }

    private sealed class AnalyzedSubtask
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
    }

    private static void MapAnalyze(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analyze", async (HttpContext context, ISessionAuthService authService, SessionAuthService auth,
            ILocalLlmClient llm, IRateLimiter limiter) =>
        {
            var ownerId = await authService.GetOwnerIdAsync();
            ApiSupport.Limit(context.Request, 40_000, "Mesaj çok uzun.");
            ApiSupport.RateLimited(limiter, $"analyze:{auth.ClientAddress()}", 30, 60);
            var input = await ApiSupport.Body<AnalyzeRequest>(context.Request);
            var message = (input.Message ?? string.Empty).Trim();
            if (message.Length is < 5 or > 8000)
                return ApiSupport.Error(400, "Lütfen en az 5 karakterlik bir mesaj girin.");

            var raw = await llm.AnalyzeAsync(ownerId, message, AnalysisSchema);
            var analyzed = ParseAnalysis(raw);
            return ApiSupport.Json(new { ticket = analyzed, mode = "ai" });
        });
    }

    private static TicketDraftInput ParseAnalysis(object raw)
    {
        var json = raw is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(raw);
        var value = JsonSerializer.Deserialize<AnalyzedTicket>(json, JsonDefaults.Options)
            ?? throw ApiException.BadRequest("Analiz sonucu beklenen biçimde değil.");
        var title = (value.Title ?? string.Empty).Trim();
        var description = (value.Description ?? string.Empty).Trim();
        if (title.Length is < 3 or > 120) throw ApiException.BadRequest("Analiz başlığı geçersiz.");
        if (description.Length is < 3 or > 2000) throw ApiException.BadRequest("Analiz açıklaması geçersiz.");
        if (!TicketTypes.Contains(value.Type ?? string.Empty)) throw ApiException.BadRequest("Analiz ticket türü geçersiz.");
        var priority = (value.Priority ?? string.Empty).ToLowerInvariant() switch
        {
            "critical" => TicketPriority.Critical,
            "high" => TicketPriority.High,
            "normal" => TicketPriority.Normal,
            _ => throw ApiException.BadRequest("Analiz önceliği geçersiz.")
        };
        var dueDate = value.DueDate ?? string.Empty;
        if (dueDate.Length > 50) throw ApiException.BadRequest("Analiz son tarihi geçersiz.");
        var labels = (value.Labels ?? new List<string>()).Select(item => (item ?? string.Empty).Trim())
            .Where(item => item.Length > 0).ToList();
        if (labels.Count > 5 || labels.Any(item => item.Length > 30)) throw ApiException.BadRequest("Analiz etiketleri geçersiz.");
        var questions = (value.Questions ?? new List<string>()).Select(item => (item ?? string.Empty).Trim())
            .Where(item => item.Length > 0).ToList();
        if (questions.Count > 2 || questions.Any(item => item.Length > 180)) throw ApiException.BadRequest("Analiz soruları geçersiz.");
        var subTasks = (value.SubTasks ?? new List<AnalyzedSubtask>()).Where(item => item is not null).ToList();
        if (subTasks.Count > 8) throw ApiException.BadRequest("Analiz alt görevleri geçersiz.");
        var normalized = new List<TicketSubtaskInput>();
        foreach (var task in subTasks)
        {
            var taskTitle = (task.Title ?? string.Empty).Trim();
            var taskDescription = (task.Description ?? string.Empty).Trim();
            if (taskTitle.Length is < 3 or > 120 || taskDescription.Length > 1000)
                throw ApiException.BadRequest("Analiz alt görevleri geçersiz.");
            normalized.Add(new TicketSubtaskInput { Title = taskTitle, Description = taskDescription });
        }
        var confidence = value.Confidence ?? 75;
        if (confidence is < 0 or > 100) throw ApiException.BadRequest("Analiz güven puanı geçersiz.");
        return new TicketDraftInput
        {
            Title = title,
            Description = description,
            Type = value.Type!,
            Priority = priority,
            DueDate = dueDate,
            Labels = labels,
            Confidence = confidence <= 1 ? (int)Math.Round(confidence * 100) : (int)Math.Round(confidence),
            Questions = questions,
            SubTasks = normalized
        };
    }
}
