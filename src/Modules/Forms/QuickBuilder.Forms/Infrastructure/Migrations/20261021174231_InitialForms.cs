using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuickBuilder.Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forms");

            migrationBuilder.CreateTable(
                name: "form_placements",
                schema: "forms",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_placements", x => new { x.site_id, x.subject_kind, x.subject_id, x.form_id });
                    table.CheckConstraint("ck_form_placements_subject_kind", "subject_kind IN ('page', 'component')");
                });

            migrationBuilder.CreateTable(
                name: "forms",
                schema: "forms",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    retention_days = table.Column<int>(type: "integer", nullable: true),
                    include_values = table.Column<bool>(type: "boolean", nullable: false),
                    recipients = table.Column<string[]>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reply_field_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    last_published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forms", x => new { x.site_id, x.form_id });
                    table.CheckConstraint("ck_forms_retention_days", "retention_days IS NULL OR retention_days IN (30, 90, 180, 365)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "forms",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.event_id, x.handler_name });
                });

            migrationBuilder.CreateTable(
                name: "rate_windows",
                schema: "forms",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    window_kind = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_windows", x => new { x.site_id, x.client_key, x.window_kind, x.window_start_utc });
                    table.CheckConstraint("ck_rate_windows_window_kind", "window_kind IN ('minute', 'hour')");
                });

            migrationBuilder.CreateTable(
                name: "spam_counters",
                schema: "forms",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    trapped_kept = table.Column<int>(type: "integer", nullable: false),
                    trapped_dropped = table.Column<int>(type: "integer", nullable: false),
                    rate_limited = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spam_counters", x => new { x.site_id, x.form_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "submission_log",
                schema: "forms",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    form_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    filters = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_log", x => x.id);
                    table.CheckConstraint("ck_submission_log_action", "action IN ('export', 'delete', 'restore', 'retention_purge', 'spam_purge')");
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    form_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    is_spam = table.Column<bool>(type: "boolean", nullable: false),
                    notification_state = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                    table.CheckConstraint("ck_submissions_notification_state", "notification_state IN ('not_needed', 'not_sent', 'pending', 'sent', 'failed', 'none')");
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "forms",
                columns: table => new
                {
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.submission_id);
                    table.CheckConstraint("ck_notification_deliveries_attempts", "attempts BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_notification_deliveries_state", "state IN ('pending', 'sending', 'sent', 'failed')");
                    table.ForeignKey(
                        name: "fk_notification_deliveries_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "forms",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_placements_site_id_form_id",
                schema: "forms",
                table: "form_placements",
                columns: new[] { "site_id", "form_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_state_next_attempt_at_utc",
                schema: "forms",
                table: "notification_deliveries",
                columns: new[] { "state", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_completed",
                schema: "forms",
                table: "outbox_messages",
                column: "completed_at_utc",
                filter: "completed_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "forms",
                table: "outbox_messages",
                column: "available_at_utc",
                filter: "completed_at_utc IS NULL AND failed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rate_windows_window_start_utc",
                schema: "forms",
                table: "rate_windows",
                column: "window_start_utc");

            migrationBuilder.CreateIndex(
                name: "ix_submission_log_site_id_at_utc",
                schema: "forms",
                table: "submission_log",
                columns: new[] { "site_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_site_id_form_id_fingerprint_received_at_utc",
                schema: "forms",
                table: "submissions",
                columns: new[] { "site_id", "form_id", "fingerprint", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_site_id_form_id_received_at_utc",
                schema: "forms",
                table: "submissions",
                columns: new[] { "site_id", "form_id", "received_at_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_site_id_is_spam_received_at_utc_id",
                schema: "forms",
                table: "submissions",
                columns: new[] { "site_id", "is_spam", "received_at_utc", "id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_placements",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "forms",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "rate_windows",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "spam_counters",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "submission_log",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "submissions",
                schema: "forms");
        }
    }
}
