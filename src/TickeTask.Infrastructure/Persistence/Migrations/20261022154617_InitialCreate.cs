using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickeTask.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => new { x.owner_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    target = table.Column<string>(type: "TEXT", nullable: false),
                    destination_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mail_senders",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_senders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    password_salt = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    approved_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    department = table.Column<string>(type: "TEXT", nullable: true),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    user_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.id);
                    table.ForeignKey(
                        name: "FK_people_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    ip_address = table.Column<string>(type: "TEXT", nullable: true),
                    serial = table.Column<string>(type: "TEXT", nullable: true),
                    location = table.Column<string>(type: "TEXT", nullable: true),
                    person_id = table.Column<string>(type: "TEXT", nullable: true),
                    parent_id = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.id);
                    table.ForeignKey(
                        name: "FK_devices_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: true),
                    person_id = table.Column<string>(type: "TEXT", nullable: true),
                    device_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: true),
                    channel = table.Column<string>(type: "TEXT", nullable: false),
                    sender = table.Column<string>(type: "TEXT", nullable: true),
                    subject = table.Column<string>(type: "TEXT", nullable: true),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    external_id = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    received_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_messages_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<string>(type: "TEXT", nullable: true),
                    person_id = table.Column<string>(type: "TEXT", nullable: true),
                    device_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: true),
                    message_id = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    priority = table.Column<string>(type: "TEXT", nullable: false),
                    due_date = table.Column<string>(type: "TEXT", nullable: true),
                    labels = table.Column<string>(type: "TEXT", nullable: false),
                    confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    analysis_mode = table.Column<string>(type: "TEXT", nullable: false),
                    target = table.Column<string>(type: "TEXT", nullable: false),
                    destination_id = table.Column<string>(type: "TEXT", nullable: false),
                    external_id = table.Column<string>(type: "TEXT", nullable: true),
                    external_url = table.Column<string>(type: "TEXT", nullable: true),
                    sub_tasks = table.Column<string>(type: "TEXT", nullable: false),
                    attachments = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.id);
                    table.ForeignKey(
                        name: "FK_tickets_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "integration_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    ticket_id = table.Column<string>(type: "TEXT", nullable: true),
                    target = table.Column<string>(type: "TEXT", nullable: false),
                    level = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_integration_logs_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mail_ingestions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    mailbox = table.Column<string>(type: "TEXT", nullable: false),
                    message_id = table.Column<string>(type: "TEXT", nullable: false),
                    sender_email = table.Column<string>(type: "TEXT", nullable: false),
                    sender_name = table.Column<string>(type: "TEXT", nullable: false),
                    subject = table.Column<string>(type: "TEXT", nullable: false),
                    body = table.Column<string>(type: "TEXT", nullable: false),
                    attachment_names = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    source_message_id = table.Column<string>(type: "TEXT", nullable: true),
                    ticket_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_ingestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_mail_ingestions_messages_source_message_id",
                        column: x => x.source_message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_mail_ingestions_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_app_settings_owner_key",
                table: "app_settings",
                columns: new[] { "owner_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_customers_owner_name",
                table: "customers",
                columns: new[] { "owner_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_devices_parent",
                table: "devices",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "idx_devices_person",
                table: "devices",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_integration_logs_ticket_id",
                table: "integration_logs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "idx_logs_owner_created",
                table: "integration_logs",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_mail_ingestions_source_message_id",
                table: "mail_ingestions",
                column: "source_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_mail_ingestions_ticket_id",
                table: "mail_ingestions",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "idx_mail_ingestions_identity",
                table: "mail_ingestions",
                columns: new[] { "owner_id", "mailbox", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_mail_ingestions_status",
                table: "mail_ingestions",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "idx_mail_senders_email",
                table: "mail_senders",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_created_by",
                table: "messages",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_messages_customer_id",
                table: "messages",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_device_id",
                table: "messages",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_person_id",
                table: "messages",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "idx_messages_owner_external",
                table: "messages",
                columns: new[] { "owner_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_messages_owner_status_received",
                table: "messages",
                columns: new[] { "owner_id", "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "idx_people_user",
                table: "people",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_sessions_token",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_sessions_user",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_created_by",
                table: "tickets",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_customer_id",
                table: "tickets",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_device_id",
                table: "tickets",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_message_id",
                table: "tickets",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_person_id",
                table: "tickets",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "idx_tickets_owner_created",
                table: "tickets",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_tickets_owner_status",
                table: "tickets",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "integration_logs");

            migrationBuilder.DropTable(
                name: "mail_ingestions");

            migrationBuilder.DropTable(
                name: "mail_senders");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
