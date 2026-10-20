using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_published_versions_subject_kind",
                schema: "pages",
                table: "published_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publish_schedules_subject_kind",
                schema: "pages",
                table: "publish_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publish_log_subject_kind",
                schema: "pages",
                table: "publish_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publications_subject_kind",
                schema: "pages",
                table: "publications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_subject_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.CreateTable(
                name: "menu_changes",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_changes", x => x.id);
                    table.CheckConstraint("ck_menu_changes_action", "action IN ('created', 'renamed', 'duplicated', 'draft_saved', 'deleted')");
                });

            migrationBuilder.CreateTable(
                name: "menus",
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
                    table.PrimaryKey("pk_menus", x => x.id);
                    table.UniqueConstraint("ak_menus_site_id_id", x => new { x.site_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "menu_drafts",
                schema: "pages",
                columns: table => new
                {
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_drafts", x => x.menu_id);
                    table.CheckConstraint("ck_menu_drafts_schema_version", "schema_version = 1");
                    table.ForeignKey(
                        name: "fk_menu_drafts_menus_menu_id",
                        column: x => x.menu_id,
                        principalSchema: "pages",
                        principalTable: "menus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_targets",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_targets", x => new { x.site_id, x.menu_id, x.target_kind, x.target_id });
                    table.CheckConstraint("ck_menu_targets_target_kind", "target_kind IN ('page', 'item')");
                    table.ForeignKey(
                        name: "fk_menu_targets_menus_site_id_menu_id",
                        columns: x => new { x.site_id, x.menu_id },
                        principalSchema: "pages",
                        principalTable: "menus",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_usages",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_usages", x => new { x.site_id, x.subject_kind, x.subject_id, x.menu_id });
                    table.CheckConstraint("ck_menu_usages_subject_kind", "subject_kind IN ('page', 'component')");
                    table.ForeignKey(
                        name: "fk_menu_usages_menus_site_id_menu_id",
                        columns: x => new { x.site_id, x.menu_id },
                        principalSchema: "pages",
                        principalTable: "menus",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_versions_subject_kind",
                schema: "pages",
                table: "published_versions",
                sql: "subject_kind IN ('page', 'component', 'menu')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publish_schedules_subject_kind",
                schema: "pages",
                table: "publish_schedules",
                sql: "subject_kind IN ('page', 'component', 'menu')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publish_log_subject_kind",
                schema: "pages",
                table: "publish_log",
                sql: "subject_kind IN ('page', 'component', 'menu', 'design')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publications_subject_kind",
                schema: "pages",
                table: "publications",
                sql: "subject_kind IN ('page', 'component', 'menu')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_subject_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "subject_kind IN ('page', 'component', 'menu')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "target_kind IN ('component', 'page', 'media', 'collection', 'template', 'menu', 'item')");

            migrationBuilder.CreateIndex(
                name: "ix_menu_changes_menu",
                schema: "pages",
                table: "menu_changes",
                columns: new[] { "site_id", "menu_id", "at_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_menu_targets_target",
                schema: "pages",
                table: "menu_targets",
                columns: new[] { "site_id", "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_usages_menu",
                schema: "pages",
                table: "menu_usages",
                columns: new[] { "site_id", "menu_id" });

            migrationBuilder.CreateIndex(
                name: "ux_menus_site_name",
                schema: "pages",
                table: "menus",
                columns: new[] { "site_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_changes",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "menu_drafts",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "menu_targets",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "menu_usages",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_published_versions_subject_kind",
                schema: "pages",
                table: "published_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publish_schedules_subject_kind",
                schema: "pages",
                table: "publish_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publish_log_subject_kind",
                schema: "pages",
                table: "publish_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publications_subject_kind",
                schema: "pages",
                table: "publications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_subject_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_versions_subject_kind",
                schema: "pages",
                table: "published_versions",
                sql: "subject_kind IN ('page', 'component')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publish_schedules_subject_kind",
                schema: "pages",
                table: "publish_schedules",
                sql: "subject_kind IN ('page', 'component')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publish_log_subject_kind",
                schema: "pages",
                table: "publish_log",
                sql: "subject_kind IN ('page', 'component', 'design')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publications_subject_kind",
                schema: "pages",
                table: "publications",
                sql: "subject_kind IN ('page', 'component')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_subject_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "subject_kind IN ('page', 'component')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "target_kind IN ('component', 'page', 'media', 'collection', 'template')");
        }
    }
}
