using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_dependencies",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_dependencies", x => new { x.site_id, x.subject_kind, x.subject_id, x.target_kind, x.target_id });
                    table.CheckConstraint("ck_live_dependencies_subject_kind", "subject_kind IN ('page', 'component')");
                    table.CheckConstraint("ck_live_dependencies_target_kind", "target_kind IN ('component', 'page', 'media')");
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "pages",
                columns: table => new
                {
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    live_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    live_number = table.Column<int>(type: "integer", nullable: true),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    published_from_draft_version = table.Column<long>(type: "bigint", nullable: true),
                    live_since_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    live_by = table.Column<Guid>(type: "uuid", nullable: true),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => new { x.subject_kind, x.subject_id });
                    table.CheckConstraint("ck_publications_live", "(live_version_id IS NULL) = (live_number IS NULL)");
                    table.CheckConstraint("ck_publications_subject_kind", "subject_kind IN ('page', 'component')");
                });

            migrationBuilder.CreateTable(
                name: "publish_log",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: true),
                    restored_from_number = table.Column<int>(type: "integer", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publish_log", x => x.id);
                    table.CheckConstraint("ck_publish_log_subject_kind", "subject_kind IN ('page', 'component', 'design')");
                });

            migrationBuilder.CreateTable(
                name: "publish_schedules",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    captured_document = table.Column<string>(type: "jsonb", nullable: true),
                    captured_schema_version = table.Column<short>(type: "smallint", nullable: true),
                    captured_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    captured_draft_version = table.Column<long>(type: "bigint", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publish_schedules", x => x.id);
                    table.CheckConstraint("ck_publish_schedules_action", "action IN ('publish', 'unpublish')");
                    table.CheckConstraint("ck_publish_schedules_state", "state IN ('pending', 'done', 'cancelled', 'failed')");
                    table.CheckConstraint("ck_publish_schedules_subject_kind", "subject_kind IN ('page', 'component')");
                    table.CheckConstraint("ck_publish_schedules_unpublish_page", "action = 'publish' OR subject_kind = 'page'");
                });

            migrationBuilder.CreateTable(
                name: "published_versions",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    restored_from_number = table.Column<int>(type: "integer", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_published_versions", x => x.id);
                    table.CheckConstraint("ck_published_versions_kind", "kind IN ('publish', 'scheduled', 'rollback')");
                    table.CheckConstraint("ck_published_versions_number", "number >= 1");
                    table.CheckConstraint("ck_published_versions_subject_kind", "subject_kind IN ('page', 'component')");
                    table.ForeignKey(
                        name: "fk_published_versions_publications_subject_kind_subject_id",
                        columns: x => new { x.subject_kind, x.subject_id },
                        principalSchema: "pages",
                        principalTable: "publications",
                        principalColumns: new[] { "subject_kind", "subject_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_dependencies_target",
                schema: "pages",
                table: "live_dependencies",
                columns: new[] { "site_id", "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_publications_site_live",
                schema: "pages",
                table: "publications",
                column: "site_id",
                filter: "live_version_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_publish_log_site_time",
                schema: "pages",
                table: "publish_log",
                columns: new[] { "site_id", "occurred_at_utc", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_publish_log_subject",
                schema: "pages",
                table: "publish_log",
                columns: new[] { "site_id", "subject_kind", "subject_id", "occurred_at_utc" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_publish_schedules_pending",
                schema: "pages",
                table: "publish_schedules",
                columns: new[] { "subject_kind", "subject_id", "action" },
                unique: true,
                filter: "state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ux_published_versions_subject_number",
                schema: "pages",
                table: "published_versions",
                columns: new[] { "subject_kind", "subject_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_dependencies",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "publish_log",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "publish_schedules",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "published_versions",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "pages");
        }
    }
}
