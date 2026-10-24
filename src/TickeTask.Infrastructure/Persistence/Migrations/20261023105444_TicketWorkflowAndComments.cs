using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickeTask.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketWorkflowAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assigned_to",
                table: "tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "resolved_at",
                table: "tickets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workflow_status",
                table: "tickets",
                type: "TEXT",
                nullable: false,
                defaultValue: "open");

            migrationBuilder.AddColumn<bool>(
                name: "is_automated",
                table: "mail_ingestions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    ticket_id = table.Column<string>(type: "TEXT", nullable: false),
                    author_id = table.Column<string>(type: "TEXT", nullable: true),
                    body = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_comments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_assigned_to",
                table: "tickets",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "idx_tickets_owner_assignee",
                table: "tickets",
                columns: new[] { "owner_id", "assigned_to" });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_owner_workflow",
                table: "tickets",
                columns: new[] { "owner_id", "workflow_status" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_author_id",
                table: "ticket_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_ticket_id",
                table: "ticket_comments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "idx_ticket_comments_owner_ticket",
                table: "ticket_comments",
                columns: new[] { "owner_id", "ticket_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_users_assigned_to",
                table: "tickets",
                column: "assigned_to",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_users_assigned_to",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_comments");

            migrationBuilder.DropIndex(
                name: "IX_tickets_assigned_to",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "idx_tickets_owner_assignee",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "idx_tickets_owner_workflow",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "assigned_to",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "workflow_status",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "is_automated",
                table: "mail_ingestions");
        }
    }
}
