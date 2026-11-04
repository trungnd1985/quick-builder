using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Sites.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_jobs",
                schema: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    trigger = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: true),
                    start_fresh = table.Column<bool>(type: "boolean", nullable: false),
                    conflict_policy = table.Column<string>(type: "text", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_site_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    new_site_host_name = table.Column<string>(type: "citext", maxLength: 253, nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    step = table.Column<string>(type: "text", nullable: true),
                    percent = table.Column<short>(type: "smallint", nullable: false),
                    progress_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archive_key = table.Column<string>(type: "text", nullable: true),
                    archive_bytes = table.Column<long>(type: "bigint", nullable: true),
                    archive_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manifest = table.Column<string>(type: "jsonb", nullable: true),
                    check_result = table.Column<string>(type: "jsonb", nullable: true),
                    check_fingerprint = table.Column<string>(type: "text", nullable: true),
                    checked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary = table.Column<string>(type: "jsonb", nullable: true),
                    report = table.Column<string>(type: "jsonb", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    pre_restore_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    lease_owner = table.Column<string>(type: "text", nullable: true),
                    lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_jobs", x => x.id);
                    table.CheckConstraint("ck_transfer_jobs_kind", "kind IN ('export', 'import')");
                    table.CheckConstraint("ck_transfer_jobs_mode", "mode IS NULL OR mode IN ('new_site', 'merge', 'restore')");
                    table.CheckConstraint("ck_transfer_jobs_percent", "percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_transfer_jobs_policy", "conflict_policy IS NULL OR conflict_policy IN ('replace', 'skip', 'rename')");
                    table.CheckConstraint("ck_transfer_jobs_trigger", "trigger IN ('admin', 'cli', 'pre_restore')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_claim",
                schema: "sites",
                table: "transfer_jobs",
                columns: new[] { "state", "created_at_utc" },
                filter: "state IN ('queued', 'confirmed')");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_expiry",
                schema: "sites",
                table: "transfer_jobs",
                column: "archive_expires_at_utc",
                filter: "archive_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_site",
                schema: "sites",
                table: "transfer_jobs",
                columns: new[] { "site_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_transfer_jobs_active_site",
                schema: "sites",
                table: "transfer_jobs",
                column: "site_id",
                unique: true,
                filter: "site_id IS NOT NULL AND trigger <> 'pre_restore' AND ((kind = 'export' AND state IN ('queued', 'running')) OR (kind = 'import' AND state IN ('confirmed', 'running')))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_jobs",
                schema: "sites");
        }
    }
}
