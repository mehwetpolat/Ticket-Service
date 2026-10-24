using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Application.Tickets;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static readonly HashSet<string> TicketTypes = new(StringComparer.Ordinal)
    {
        "Hata", "Talep", "Yeni özellik", "Değişiklik"
    };

    private static void MapTickets(IEndpointRouteBuilder app)
    {
        MapTicketPublish(app);
        var group = app.MapGroup("/api/tickets");

        group.MapGet("/", async (ISessionAuthService auth, ITicketRepository tickets) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var rows = await tickets.ListAsync(ownerId, actor.Role == UserRole.Admin, actor.Id);
            return ApiSupport.Json(new
            {
                tickets = rows.Select(ticket => new TicketDto(
                    ticket.Id, ticket.MessageId, ticket.Title, ticket.Description, ticket.Type, ticket.Priority,
                    ticket.DueDate, JsonList<string>(ticket.LabelsJson), JsonList<TicketSubtask>(ticket.SubTasksJson),
                    ticket.Target, ticket.DestinationId, ticket.ExternalId, ticket.ExternalUrl, ticket.Status,
                    ticket.WorkflowStatus, ticket.AssignedTo, ticket.AssignedToName, ticket.ResolvedAt,
                    ticket.CommentCount, ticket.Error, ticket.CreatedAt, ticket.CustomerName, ticket.PersonId,
                    ticket.PersonName, ticket.DeviceId, ticket.DeviceName, ticket.CreatedBy, ticket.CreatedByName))
            });
        });

        group.MapPatch("/{id}", async (string id, ISessionAuthService auth, ITicketRepository tickets,
            IUserRepository users, IUnitOfWork unitOfWork, IClock clock, HttpRequest request) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var ticket = await tickets.GetAsync(id, ownerId) ?? throw ApiException.NotFound("Ticket bulunamadı.");
            var manager = actor.Role == UserRole.Admin || ticket.CreatedBy == actor.Id;
            var assignee = ticket.AssignedTo == actor.Id;
            if (!manager && !assignee) return ApiSupport.Error(403, "Bu ticket için yetkiniz yok.");

            var input = await ApiSupport.Body<UpdateTicketRequest>(request);
            var workflowOnly = !manager;
            if (workflowOnly && (input.Title is not null || input.Description is not null || input.Type is not null ||
                input.Priority is not null || input.DueDate is not null || input.Labels is not null || input.AssignedTo is not null))
                return ApiSupport.Error(403, "Bu ticket üzerinde yalnızca durum güncelleyebilirsiniz.");

            if (input.Title is not null) ticket.Title = Guard.Text(input.Title, 3, 120, "Başlık 3-120 karakter olmalı.");
            if (input.Description is not null) ticket.Description = Guard.Text(input.Description, 3, 12_000, "Açıklama 3-12000 karakter olmalı.");
            if (input.Type is not null)
            {
                if (!TicketTypes.Contains(input.Type)) throw ApiException.BadRequest("Ticket türü geçersiz.");
                ticket.Type = input.Type;
            }
            if (input.Priority is { } priority)
            {
                if (!Enum.IsDefined(priority)) throw ApiException.BadRequest("Öncelik geçersiz.");
                ticket.Priority = priority;
            }
            if (input.DueDate is not null)
            {
                if (input.DueDate.Length > 0 && !DateOnly.TryParseExact(input.DueDate, "yyyy-MM-dd", out _))
                    throw ApiException.BadRequest("Son tarih YYYY-MM-DD biçiminde olmalı.");
                ticket.DueDate = string.IsNullOrWhiteSpace(input.DueDate) ? null : input.DueDate;
            }
            if (input.Labels is not null)
            {
                var labels = input.Labels.Select(label => (label ?? string.Empty).Trim()).Where(label => label.Length > 0).ToList();
                if (labels.Count > 5 || labels.Any(label => label.Length > 30))
                    throw ApiException.BadRequest("En fazla 5 etiket ve her etiket en fazla 30 karakter olabilir.");
                ticket.LabelsJson = JsonSerializer.Serialize(labels);
            }
            if (input.AssignedTo is not null)
            {
                var assignedTo = input.AssignedTo.Trim();
                if (assignedTo.Length > 0)
                {
                    var user = await users.GetByIdAsync(assignedTo);
                    if (user is null || user.Status != UserStatus.Active)
                        return ApiSupport.Error(400, "Atanacak kullanıcı bulunamadı.");
                    ticket.AssignedTo = user.Id;
                }
                else
                {
                    ticket.AssignedTo = null;
                }
            }
            if (input.WorkflowStatus is { } workflow)
            {
                if (!Enum.IsDefined(workflow)) throw ApiException.BadRequest("Ticket durumu geçersiz.");
                ticket.WorkflowStatus = workflow;
                ticket.ResolvedAt = workflow is TicketWorkflowStatus.Resolved or TicketWorkflowStatus.Closed
                    ? ticket.ResolvedAt ?? clock.UnixNow
                    : null;
            }
            ticket.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync();
            return ApiSupport.Json(new { ok = true });
        });

        group.MapGet("/{id}/comments", async (string id, ISessionAuthService auth, ITicketRepository tickets) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var ticket = await tickets.GetAsync(id, ownerId) ?? throw ApiException.NotFound("Ticket bulunamadı.");
            if (actor.Role != UserRole.Admin && ticket.CreatedBy != actor.Id && ticket.AssignedTo != actor.Id)
                return ApiSupport.Error(403, "Bu ticket için yetkiniz yok.");
            var comments = await tickets.ListCommentsAsync(id, ownerId);
            return ApiSupport.Json(new
            {
                comments = comments.Select(comment => new TicketCommentDto(
                    comment.Id, comment.TicketId, comment.AuthorId, comment.AuthorName, comment.Body, comment.CreatedAt))
            });
        });

        group.MapPost("/{id}/comments", async (string id, ISessionAuthService auth, ITicketRepository tickets,
            IUnitOfWork unitOfWork, IClock clock, IIdGenerator ids, HttpRequest request) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var ticket = await tickets.GetAsync(id, ownerId) ?? throw ApiException.NotFound("Ticket bulunamadı.");
            if (actor.Role != UserRole.Admin && ticket.CreatedBy != actor.Id && ticket.AssignedTo != actor.Id)
                return ApiSupport.Error(403, "Bu ticket için yetkiniz yok.");
            var input = await ApiSupport.Body<AddTicketCommentRequest>(request);
            var body = Guard.Text(input.Body, 1, 4000, "Not 1-4000 karakter olmalı.");
            var now = clock.UnixNow;
            var comment = new TicketComment
            {
                Id = ids.Create("cmt"),
                OwnerId = ownerId,
                TicketId = ticket.Id,
                AuthorId = actor.Id,
                Body = body,
                CreatedAt = now
            };
            await tickets.AddCommentAsync(comment);
            ticket.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync();
            return ApiSupport.Json(new TicketCommentDto(comment.Id, comment.TicketId, actor.Id, actor.Name, comment.Body, comment.CreatedAt), 201);
        });

        group.MapPost("/", async (HttpContext context, ISessionAuthService auth, ITicketRepository tickets,
            IMessageRepository messages, IPersonRepository people, IDeviceRepository devices,
            ICustomerRepository customers, IProjectService project, ITicketPublisher publisher,
            IIntegrationLogRepository logs, IUnitOfWork unitOfWork, IClock clock, ITicketTargetCatalog catalog,
            IIdGenerator ids, HttpRequest request) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            ApiSupport.Limit(request, 14_000_000, "Eklerin toplam boyutu çok büyük.");
            var input = await ApiSupport.Body<CreateTicketRequest>(request);
            input.Draft ??= new TicketDraftInput();
            input.Attachments ??= new List<TicketAttachmentInput>();

            if (actor.Role != UserRole.Admin && input.CustomerId is not null)
                return ApiSupport.Error(403, "Harici hedef seçme yetkiniz yok.");
            if (input.Attachments.Count > 3) throw ApiException.BadRequest("En fazla 3 ek gönderilebilir.");
            long totalBytes = 0;
            foreach (var attachment in input.Attachments)
            {
                attachment.Name = Guard.Text(attachment.Name, 1, 180, "Ek adı 1-180 karakter olmalı.");
                if ((attachment.ContentType ?? string.Empty).Length > 100) throw ApiException.BadRequest("Ek türü çok uzun.");
                var data = attachment.Data ?? string.Empty;
                if (data.Length > 7_000_000) throw ApiException.BadRequest("Ek 5 MB sınırını aşıyor.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(data); }
                catch (FormatException) { throw ApiException.BadRequest("Ek verisi geçersiz."); }
                if (bytes.Length > 5 * 1024 * 1024) throw ApiException.BadRequest("Ek 5 MB sınırını aşıyor.");
                totalBytes += bytes.Length;
            }
            if (totalBytes > 10 * 1024 * 1024) return ApiSupport.Error(413, "Eklerin toplamı 10 MB sınırını aşıyor.");

            string? personId = input.PersonId;
            if (actor.Role == UserRole.Member)
                personId = (await people.GetByUserIdAsync(actor.Id))?.Id;
            Person? person = null;
            if (!string.IsNullOrWhiteSpace(personId))
            {
                person = await people.GetByIdAsync(personId);
                if (person is null) return ApiSupport.Error(400, "Çalışan bulunamadı.");
            }

            Device? device = null;
            string? parentName = null;
            if (!string.IsNullOrWhiteSpace(input.DeviceId))
            {
                device = await devices.GetByIdAsync(input.DeviceId);
                if (device is null || device.PersonId != personId) return ApiSupport.Error(400, "Seçilen cihaz bu çalışana bağlı değil.");
                if (!string.IsNullOrWhiteSpace(device.ParentId))
                    parentName = (await devices.GetByIdAsync(device.ParentId))?.Name;
            }

            Customer? customer = null;
            if (!string.IsNullOrWhiteSpace(input.CustomerId))
            {
                customer = await customers.GetAsync(input.CustomerId, ownerId);
                if (customer is null) return ApiSupport.Error(404, "Kayıtlı taraf bulunamadı.");
            }

            var projectConfig = await project.GetAsync(ownerId);
            var target = customer?.Target ?? projectConfig.Target;
            var destinationId = customer?.DestinationId ?? projectConfig.DestinationId;
            if (input.Publish && string.IsNullOrWhiteSpace(destinationId))
                return ApiSupport.Error(400, $"Yönetici, Proje yönetimi ayarlarında {catalog.Get(target).DestinationLabel} alanını tanımlamalı.");
            if (input.Attachments.Count > 0 && target != TicketTarget.Jira)
                return ApiSupport.Error(400, "Ek gönderimi şu anda yalnızca Jira için kullanılabilir.");

            Message? sourceMessage = null;
            if (!string.IsNullOrWhiteSpace(input.MessageId))
            {
                sourceMessage = await messages.GetAsync(input.MessageId, ownerId);
                if (sourceMessage is null ||
                    (actor.Role == UserRole.Member && sourceMessage.CreatedBy != actor.Id) ||
                    sourceMessage.CustomerId != input.CustomerId)
                    return ApiSupport.Error(400, "Mesaj erişimi veya eşleşmesi geçersiz.");
            }

            var draft = input.Draft;
            var title = Guard.Text(draft.Title, 3, 120, "Başlık 3-120 karakter olmalı.");
            var description = Guard.Text(draft.Description, 3, 4000, "Açıklama 3-4000 karakter olmalı.");
            if (!TicketTypes.Contains(draft.Type ?? string.Empty)) throw ApiException.BadRequest("Ticket türü geçersiz.");
            if (!Enum.IsDefined(draft.Priority)) throw ApiException.BadRequest("Öncelik geçersiz.");
            if (!string.IsNullOrEmpty(draft.DueDate) && !DateOnly.TryParseExact(draft.DueDate, "yyyy-MM-dd", out _))
                throw ApiException.BadRequest("Son tarih YYYY-MM-DD biçiminde olmalı.");
            draft.Labels ??= new List<string>();
            if (draft.Labels.Count > 5 || draft.Labels.Any(label => (label ?? string.Empty).Length > 30))
                throw ApiException.BadRequest("En fazla 5 etiket ve her etiket en fazla 30 karakter olabilir.");
            draft.SubTasks ??= new List<TicketSubtaskInput>();
            if (draft.SubTasks.Count > 8) throw ApiException.BadRequest("En fazla 8 alt görev eklenebilir.");
            foreach (var task in draft.SubTasks)
            {
                task.Title = Guard.Text(task.Title, 3, 120, "Alt görev başlığı 3-120 karakter olmalı.");
                task.Description ??= string.Empty;
                if (task.Description.Length > 1000) throw ApiException.BadRequest("Alt görev notu çok uzun.");
            }

            var contextLines = new[]
            {
                $"Bildiren: {actor.Name} ({actor.Email})",
                customer is not null ? $"Kayıtlı taraf: {customer.Name}" : string.Empty,
                person is not null ? $"Etkilenen çalışan: {person.Name}{(!string.IsNullOrWhiteSpace(person.Department) ? $" / {person.Department}" : string.Empty)}{(!string.IsNullOrWhiteSpace(person.Email) ? $" / {person.Email}" : string.Empty)}" : string.Empty,
                device is not null ? $"Cihaz: {device.Name} ({device.Kind})" : string.Empty,
                !string.IsNullOrWhiteSpace(device?.IpAddress) ? $"İç IP: {device.IpAddress}" : string.Empty,
                !string.IsNullOrWhiteSpace(device?.Serial) ? $"Seri / envanter: {device.Serial}" : string.Empty,
                !string.IsNullOrWhiteSpace(device?.Location) ? $"Konum: {device.Location}" : string.Empty,
                !string.IsNullOrWhiteSpace(parentName) ? $"Bağlı cihaz: {parentName}" : string.Empty
            };
            var descriptionWithContext = TicketContextFormatter.AppendContext(description, contextLines);
            if (sourceMessage is not null)
            {
                var senderName = person?.Name ?? actor.Name;
                var senderEmail = person?.Email ?? actor.Email;
                var storedSender = sourceMessage.Sender ?? string.Empty;
                var open = storedSender.LastIndexOf('<');
                if (open > 0 && storedSender.EndsWith('>'))
                {
                    senderName = storedSender[..open].Trim().Trim('"');
                    senderEmail = storedSender[(open + 1)..^1].Trim();
                }
                descriptionWithContext = TicketContextFormatter.AppendSource(descriptionWithContext, senderName, senderEmail,
                    sourceMessage.Subject, sourceMessage.ExternalId, input.Attachments.Select(attachment => attachment.Name).ToList(),
                    sourceMessage.Content);
            }

            var ticketId = ids.Create("tkt");
            var now = clock.UnixNow;
            var subTasks = draft.SubTasks.Select(task => new TicketSubtask { Title = task.Title.Trim(), Description = (task.Description ?? string.Empty).Trim() }).ToList();
            var ticket = new Ticket
            {
                Id = ticketId,
                OwnerId = ownerId,
                CustomerId = input.CustomerId,
                PersonId = personId,
                DeviceId = input.DeviceId,
                CreatedBy = actor.Id,
                MessageId = input.MessageId,
                Title = title,
                Description = descriptionWithContext,
                Type = draft.Type!,
                Priority = draft.Priority,
                DueDate = string.IsNullOrWhiteSpace(draft.DueDate) ? null : draft.DueDate,
                LabelsJson = JsonSerializer.Serialize(draft.Labels),
                Confidence = draft.Confidence,
                AnalysisMode = input.AnalysisMode,
                Target = target,
                DestinationId = destinationId,
                SubTasksJson = JsonSerializer.Serialize(subTasks, JsonDefaults.Options),
                AttachmentsJson = JsonSerializer.Serialize(input.Attachments, JsonDefaults.Options),
                Status = TicketStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };
            await tickets.AddAsync(ticket);
            if (!input.Publish) return ApiSupport.Json(new CreateTicketResponse(ticketId, "draft"), 201);

            var payload = new TicketDraft
            {
                Title = title,
                Description = descriptionWithContext,
                Type = draft.Type!,
                Priority = draft.Priority,
                DueDate = draft.DueDate ?? string.Empty,
                Labels = draft.Labels.ToList(),
                Confidence = draft.Confidence,
                Questions = draft.Questions?.ToList() ?? new List<string>(),
                SubTasks = subTasks
            };
            try
            {
                var published = await publisher.PublishAsync(ownerId, ticketId, target, destinationId, payload);
                ticket.Status = TicketStatus.Published;
                ticket.ExternalId = published.ExternalId;
                ticket.ExternalUrl = published.ExternalUrl;
                ticket.Error = null;
                ticket.UpdatedAt = clock.UnixNow;
                await logs.AddAsync(new IntegrationLog
                {
                    Id = ids.Create("log"),
                    OwnerId = ownerId,
                    TicketId = ticketId,
                    Target = catalog.Get(target).Key,
                    Level = IntegrationLogLevel.Info,
                    Message = $"{published.ExternalId} oluşturuldu.",
                    CreatedAt = clock.UnixNow
                });
                if (!string.IsNullOrWhiteSpace(input.MessageId))
                {
                    var message = await messages.GetAsync(input.MessageId, ownerId);
                    if (message is not null) message.Status = MessageStatus.Converted;
                }
                await unitOfWork.SaveChangesAsync();
                return ApiSupport.Json(new CreateTicketResponse(ticketId, "published", published.ExternalId, published.ExternalUrl), 201);
            }
            catch (Exception exception)
            {
                var message = exception is ApiException ? exception.Message : "Dış sistemde ticket oluşturulamadı.";
                ticket.Status = TicketStatus.Failed;
                ticket.Error = message;
                ticket.UpdatedAt = clock.UnixNow;
                await logs.AddAsync(new IntegrationLog
                {
                    Id = ids.Create("log"),
                    OwnerId = ownerId,
                    TicketId = ticketId,
                    Target = catalog.Get(target).Key,
                    Level = IntegrationLogLevel.Error,
                    Message = message,
                    CreatedAt = clock.UnixNow
                });
                return ApiSupport.Json(new CreateTicketResponse(ticketId, "failed", Error: message), 502);
            }
        });
    }

    private static void MapTicketPublish(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tickets/{id}/publish", async (string id, ISessionAuthService auth, ITicketRepository tickets,
            IProjectService project, ITicketPublisher publisher, IIntegrationLogRepository logs,
            IUnitOfWork unitOfWork, IClock clock, ITicketTargetCatalog catalog, IIdGenerator ids) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var ticket = await tickets.GetAsync(id, ownerId) ?? throw ApiException.NotFound("Ticket bulunamadı.");
            if (actor.Role != UserRole.Admin && ticket.CreatedBy != actor.Id)
                return ApiSupport.Error(403, "Bu ticket için yetkiniz yok.");
            if (ticket.Status == TicketStatus.Published)
                return ApiSupport.Error(409, "Bu ticket zaten dış sistemde oluşturulmuş.");

            var projectConfig = await project.GetAsync(ownerId);
            var destinationId = !string.IsNullOrWhiteSpace(ticket.DestinationId)
                ? ticket.DestinationId
                : string.IsNullOrWhiteSpace(ticket.CustomerId) && projectConfig.Target == ticket.Target
                    ? projectConfig.DestinationId
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(destinationId)) return ApiSupport.Error(400, "Önce proje hedefini ayarlayın.");

            var draft = new TicketDraft
            {
                Title = ticket.Title,
                Description = ticket.Description,
                Type = ticket.Type,
                Priority = ticket.Priority,
                DueDate = ticket.DueDate ?? string.Empty,
                Labels = JsonList<string>(ticket.LabelsJson),
                Confidence = ticket.Confidence,
                Questions = new List<string>(),
                SubTasks = JsonList<TicketSubtask>(ticket.SubTasksJson)
            };
            try
            {
                var published = await publisher.PublishAsync(ownerId, ticket.Id, ticket.Target, destinationId, draft);
                ticket.Status = TicketStatus.Published;
                ticket.ExternalId = published.ExternalId;
                ticket.ExternalUrl = published.ExternalUrl;
                ticket.Error = null;
                ticket.UpdatedAt = clock.UnixNow;
                await logs.AddAsync(new IntegrationLog
                {
                    Id = ids.Create("log"),
                    OwnerId = ownerId,
                    TicketId = ticket.Id,
                    Target = catalog.Get(ticket.Target).Key,
                    Level = IntegrationLogLevel.Info,
                    Message = $"{published.ExternalId} oluşturuldu.",
                    CreatedAt = clock.UnixNow
                });
                return ApiSupport.Json(new PublishResponse("published", published.ExternalId, published.ExternalUrl));
            }
            catch (Exception exception)
            {
                var message = exception is ApiException ? exception.Message : "Dış sistem hatası.";
                ticket.Status = TicketStatus.Failed;
                ticket.Error = message;
                ticket.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync();
                return ApiSupport.Error(502, message);
            }
        });
    }

    private static List<T> JsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try { return JsonSerializer.Deserialize<List<T>>(json, JsonDefaults.Options) ?? new List<T>(); }
        catch { return new List<T>(); }
    }
}
