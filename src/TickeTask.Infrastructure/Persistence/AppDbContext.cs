using Microsoft.EntityFrameworkCore;
using TickeTask.Domain.Entities;

namespace TickeTask.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<MailSender> MailSenders => Set<MailSender>();
    public DbSet<MailIngestion> MailIngestions => Set<MailIngestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
