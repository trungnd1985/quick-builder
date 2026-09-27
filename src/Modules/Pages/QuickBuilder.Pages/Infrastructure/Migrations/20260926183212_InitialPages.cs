using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pages");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "components",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_components", x => x.id);
                    table.UniqueConstraint("ak_components_site_id_id", x => new { x.site_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "pages",
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
                name: "page_trees",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_trees", x => x.site_id);
                    table.CheckConstraint("ck_page_trees_page_count", "page_count BETWEEN 0 AND 2000");
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "pages",
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
                name: "component_drafts",
                schema: "pages",
                columns: table => new
                {
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_component_drafts", x => x.component_id);
                    table.CheckConstraint("ck_component_drafts_schema_version", "schema_version = 1");
                    table.ForeignKey(
                        name: "fk_component_drafts_components_component_id",
                        column: x => x.component_id,
                        principalSchema: "pages",
                        principalTable: "components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pages",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    depth = table.Column<short>(type: "smallint", nullable: false),
                    is_home = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pages", x => x.id);
                    table.UniqueConstraint("ak_pages_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_pages_depth", "depth BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_pages_home_top_level", "NOT is_home OR (parent_id IS NULL AND path = '/')");
                    table.CheckConstraint("ck_pages_position", "position >= 0");
                    table.CheckConstraint("ck_pages_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "fk_pages_page_trees_site_id",
                        column: x => x.site_id,
                        principalSchema: "pages",
                        principalTable: "page_trees",
                        principalColumn: "site_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pages_pages_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "pages",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "component_usages",
                schema: "pages",
                columns: table => new
                {
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_component_usages", x => new { x.page_id, x.component_id });
                    table.ForeignKey(
                        name: "fk_component_usages_components_site_id_component_id",
                        columns: x => new { x.site_id, x.component_id },
                        principalSchema: "pages",
                        principalTable: "components",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_component_usages_pages_site_id_page_id",
                        columns: x => new { x.site_id, x.page_id },
                        principalSchema: "pages",
                        principalTable: "pages",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_drafts",
                schema: "pages",
                columns: table => new
                {
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_drafts", x => x.page_id);
                    table.CheckConstraint("ck_page_drafts_schema_version", "schema_version = 1");
                    table.ForeignKey(
                        name: "fk_page_drafts_pages_page_id",
                        column: x => x.page_id,
                        principalSchema: "pages",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_component_usages_site_component",
                schema: "pages",
                table: "component_usages",
                columns: new[] { "site_id", "component_id" });

            migrationBuilder.CreateIndex(
                name: "ix_component_usages_site_id_page_id",
                schema: "pages",
                table: "component_usages",
                columns: new[] { "site_id", "page_id" });

            migrationBuilder.CreateIndex(
                name: "ux_components_site_name",
                schema: "pages",
                table: "components",
                columns: new[] { "site_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_completed",
                schema: "pages",
                table: "outbox_messages",
                column: "completed_at_utc",
                filter: "completed_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "pages",
                table: "outbox_messages",
                column: "available_at_utc",
                filter: "completed_at_utc IS NULL AND failed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pages_parent_id",
                schema: "pages",
                table: "pages",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_site_parent_position",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ux_pages_site_home",
                schema: "pages",
                table: "pages",
                column: "site_id",
                unique: true,
                filter: "is_home");

            migrationBuilder.CreateIndex(
                name: "ux_pages_site_path",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "component_drafts",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "component_usages",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "page_drafts",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "components",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "pages",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "page_trees",
                schema: "pages");
        }
    }
}
