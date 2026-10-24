using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Infrastructure.Persistence.Conversions;

namespace TickeTask.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.Email).HasColumnName("email").IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.PasswordSalt).HasColumnName("password_salt").IsRequired();
        builder.Property(entity => entity.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(entity => entity.Role).HasColumnName("role").HasConversion(EnumConverters.LowerString<UserRole>()).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion(EnumConverters.LowerString<UserStatus>()).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.ApprovedAt).HasColumnName("approved_at");
        builder.HasIndex(entity => entity.Email).IsUnique().HasDatabaseName("idx_users_email");
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entity => entity.TokenHash).IsUnique().HasDatabaseName("idx_sessions_token");
        builder.HasIndex(entity => entity.UserId).HasDatabaseName("idx_sessions_user");
        builder.HasOne(entity => entity.User).WithMany(entity => entity.Sessions).HasForeignKey(entity => entity.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Email).HasColumnName("email");
        builder.Property(entity => entity.Department).HasColumnName("department");
        builder.Property(entity => entity.Phone).HasColumnName("phone");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => entity.UserId).IsUnique().HasDatabaseName("idx_people_user");
        builder.HasOne(entity => entity.User).WithMany().HasForeignKey(entity => entity.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Kind).HasColumnName("kind").IsRequired();
        builder.Property(entity => entity.IpAddress).HasColumnName("ip_address");
        builder.Property(entity => entity.Serial).HasColumnName("serial");
        builder.Property(entity => entity.Location).HasColumnName("location");
        builder.Property(entity => entity.PersonId).HasColumnName("person_id");
        builder.Property(entity => entity.ParentId).HasColumnName("parent_id");
        builder.Property(entity => entity.Notes).HasColumnName("notes");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => entity.PersonId).HasDatabaseName("idx_devices_person");
        builder.HasIndex(entity => entity.ParentId).HasDatabaseName("idx_devices_parent");
        builder.HasOne(entity => entity.Person).WithMany(entity => entity.Devices).HasForeignKey(entity => entity.PersonId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Email).HasColumnName("email");
        builder.Property(entity => entity.Phone).HasColumnName("phone");
        builder.Property(entity => entity.Target).HasColumnName("target").HasConversion(EnumConverters.LowerString<TicketTarget>()).IsRequired();
        builder.Property(entity => entity.DestinationId).HasColumnName("destination_id");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.Name }).IsUnique().HasDatabaseName("idx_customers_owner_name");
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.CustomerId).HasColumnName("customer_id");
        builder.Property(entity => entity.PersonId).HasColumnName("person_id");
        builder.Property(entity => entity.DeviceId).HasColumnName("device_id");
        builder.Property(entity => entity.CreatedBy).HasColumnName("created_by");
        builder.Property(entity => entity.Channel).HasColumnName("channel").HasConversion(EnumConverters.LowerString<MessageChannel>()).IsRequired();
        builder.Property(entity => entity.Sender).HasColumnName("sender");
        builder.Property(entity => entity.Subject).HasColumnName("subject");
        builder.Property(entity => entity.Content).HasColumnName("content");
        builder.Property(entity => entity.ExternalId).HasColumnName("external_id");
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion(EnumConverters.LowerString<MessageStatus>()).IsRequired();
        builder.Property(entity => entity.ReceivedAt).HasColumnName("received_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.Status, entity.ReceivedAt }).HasDatabaseName("idx_messages_owner_status_received");
        builder.HasIndex(entity => new { entity.OwnerId, entity.ExternalId }).IsUnique().HasDatabaseName("idx_messages_owner_external");
        builder.HasOne(entity => entity.Customer).WithMany().HasForeignKey(entity => entity.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.Person).WithMany().HasForeignKey(entity => entity.PersonId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.Device).WithMany().HasForeignKey(entity => entity.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.CreatedByUser).WithMany().HasForeignKey(entity => entity.CreatedBy).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.CustomerId).HasColumnName("customer_id");
        builder.Property(entity => entity.PersonId).HasColumnName("person_id");
        builder.Property(entity => entity.DeviceId).HasColumnName("device_id");
        builder.Property(entity => entity.CreatedBy).HasColumnName("created_by");
        builder.Property(entity => entity.AssignedTo).HasColumnName("assigned_to");
        builder.Property(entity => entity.MessageId).HasColumnName("message_id");
        builder.Property(entity => entity.Title).HasColumnName("title").IsRequired();
        builder.Property(entity => entity.Description).HasColumnName("description").IsRequired();
        builder.Property(entity => entity.Type).HasColumnName("type").IsRequired();
        builder.Property(entity => entity.Priority).HasColumnName("priority").HasConversion(EnumConverters.LowerString<TicketPriority>()).IsRequired();
        builder.Property(entity => entity.DueDate).HasColumnName("due_date");
        builder.Property(entity => entity.LabelsJson).HasColumnName("labels").IsRequired();
        builder.Property(entity => entity.Confidence).HasColumnName("confidence");
        builder.Property(entity => entity.AnalysisMode).HasColumnName("analysis_mode").HasConversion(EnumConverters.LowerString<AnalysisMode>()).IsRequired();
        builder.Property(entity => entity.Target).HasColumnName("target").HasConversion(EnumConverters.LowerString<TicketTarget>()).IsRequired();
        builder.Property(entity => entity.DestinationId).HasColumnName("destination_id");
        builder.Property(entity => entity.ExternalId).HasColumnName("external_id");
        builder.Property(entity => entity.ExternalUrl).HasColumnName("external_url");
        builder.Property(entity => entity.SubTasksJson).HasColumnName("sub_tasks").IsRequired();
        builder.Property(entity => entity.AttachmentsJson).HasColumnName("attachments").IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion(EnumConverters.LowerString<TicketStatus>()).IsRequired();
        builder.Property(entity => entity.WorkflowStatus).HasColumnName("workflow_status").HasConversion(EnumConverters.SnakeString<TicketWorkflowStatus>()).IsRequired();
        builder.Property(entity => entity.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(entity => entity.Error).HasColumnName("error");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt }).HasDatabaseName("idx_tickets_owner_created");
        builder.HasIndex(entity => new { entity.OwnerId, entity.Status }).HasDatabaseName("idx_tickets_owner_status");
        builder.HasIndex(entity => new { entity.OwnerId, entity.WorkflowStatus }).HasDatabaseName("idx_tickets_owner_workflow");
        builder.HasIndex(entity => new { entity.OwnerId, entity.AssignedTo }).HasDatabaseName("idx_tickets_owner_assignee");
        builder.HasOne(entity => entity.Customer).WithMany().HasForeignKey(entity => entity.CustomerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.Person).WithMany().HasForeignKey(entity => entity.PersonId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.Device).WithMany().HasForeignKey(entity => entity.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.CreatedByUser).WithMany().HasForeignKey(entity => entity.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.AssignedToUser).WithMany().HasForeignKey(entity => entity.AssignedTo).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.SourceMessage).WithMany().HasForeignKey(entity => entity.MessageId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.TicketId).HasColumnName("ticket_id");
        builder.Property(entity => entity.AuthorId).HasColumnName("author_id");
        builder.Property(entity => entity.Body).HasColumnName("body").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.TicketId, entity.CreatedAt }).HasDatabaseName("idx_ticket_comments_owner_ticket");
        builder.HasOne(entity => entity.Ticket).WithMany(ticket => ticket.Comments).HasForeignKey(entity => entity.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.Author).WithMany().HasForeignKey(entity => entity.AuthorId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class IntegrationLogConfiguration : IEntityTypeConfiguration<IntegrationLog>
{
    public void Configure(EntityTypeBuilder<IntegrationLog> builder)
    {
        builder.ToTable("integration_logs");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.TicketId).HasColumnName("ticket_id");
        builder.Property(entity => entity.Target).HasColumnName("target");
        builder.Property(entity => entity.Level).HasColumnName("level").HasConversion(EnumConverters.LowerString<IntegrationLogLevel>()).IsRequired();
        builder.Property(entity => entity.Message).HasColumnName("message");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt }).HasDatabaseName("idx_logs_owner_created");
        builder.HasOne<Ticket>().WithMany().HasForeignKey(entity => entity.TicketId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(entity => new { entity.OwnerId, entity.Key });
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.Key).HasColumnName("key");
        builder.Property(entity => entity.Value).HasColumnName("value");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.Key }).IsUnique().HasDatabaseName("idx_app_settings_owner_key");
    }
}

public sealed class MailSenderConfiguration : IEntityTypeConfiguration<MailSender>
{
    public void Configure(EntityTypeBuilder<MailSender> builder)
    {
        builder.ToTable("mail_senders");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.Email).HasColumnName("email").IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Active).HasColumnName("active");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => entity.Email).IsUnique().HasDatabaseName("idx_mail_senders_email");
    }
}

public sealed class MailIngestionConfiguration : IEntityTypeConfiguration<MailIngestion>
{
    public void Configure(EntityTypeBuilder<MailIngestion> builder)
    {
        builder.ToTable("mail_ingestions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        builder.Property(entity => entity.Mailbox).HasColumnName("mailbox");
        builder.Property(entity => entity.MessageId).HasColumnName("message_id");
        builder.Property(entity => entity.SenderEmail).HasColumnName("sender_email");
        builder.Property(entity => entity.SenderName).HasColumnName("sender_name");
        builder.Property(entity => entity.Subject).HasColumnName("subject");
        builder.Property(entity => entity.Body).HasColumnName("body");
        builder.Property(entity => entity.AttachmentNamesJson).HasColumnName("attachment_names");
        builder.Property(entity => entity.IsAutomated).HasColumnName("is_automated");
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion(EnumConverters.SnakeString<MailIngestionStatus>()).IsRequired();
        builder.Property(entity => entity.Attempts).HasColumnName("attempts");
        builder.Property(entity => entity.Error).HasColumnName("error");
        builder.Property(entity => entity.SourceMessageId).HasColumnName("source_message_id");
        builder.Property(entity => entity.TicketId).HasColumnName("ticket_id");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => new { entity.OwnerId, entity.Mailbox, entity.MessageId }).IsUnique().HasDatabaseName("idx_mail_ingestions_identity");
        builder.HasIndex(entity => new { entity.Status, entity.UpdatedAt }).HasDatabaseName("idx_mail_ingestions_status");
        builder.HasOne(entity => entity.Ticket).WithMany().HasForeignKey(entity => entity.TicketId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Message>().WithMany().HasForeignKey(entity => entity.SourceMessageId).OnDelete(DeleteBehavior.SetNull);
    }
}
