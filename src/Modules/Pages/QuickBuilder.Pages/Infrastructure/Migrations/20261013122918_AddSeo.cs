using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "redirect_to_page_id",
                schema: "pages",
                table: "publish_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "redirect_changes",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redirect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redirect_changes", x => x.id);
                    table.CheckConstraint("ck_redirect_changes_action", "action IN ('created', 'changed', 'turned_on', 'turned_off', 'deleted', 'imported', 'offered', 'retargeted')");
                    table.CheckConstraint("ck_redirect_changes_origin", "origin IN ('manual', 'import', 'offer')");
                });

            migrationBuilder.CreateTable(
                name: "redirects",
                schema: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_query = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status_code = table.Column<short>(type: "smallint", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_page_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_path = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redirects", x => x.id);
                    table.CheckConstraint("ck_redirects_match_kind", "match_kind IN ('exact', 'prefix')");
                    table.CheckConstraint("ck_redirects_origin", "origin IN ('manual', 'import', 'offer')");
                    table.CheckConstraint("ck_redirects_prefix", "match_kind = 'exact' OR (source_query = '' AND target_kind IN ('page', 'path'))");
                    table.CheckConstraint("ck_redirects_status_code", "status_code IN (301, 302)");
                    table.CheckConstraint("ck_redirects_target", "(target_kind = 'page' AND target_page_id IS NOT NULL AND target_item_id IS NULL AND target_path IS NULL AND target_url IS NULL) OR (target_kind = 'item' AND target_collection_id IS NOT NULL AND target_item_id IS NOT NULL AND target_page_id IS NULL AND target_path IS NULL AND target_url IS NULL) OR (target_kind = 'path' AND target_path IS NOT NULL AND target_page_id IS NULL AND target_item_id IS NULL AND target_url IS NULL) OR (target_kind = 'url' AND target_url IS NOT NULL AND target_page_id IS NULL AND target_item_id IS NULL AND target_path IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "sitemap_state",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sitemap_state", x => x.site_id);
                });

            migrationBuilder.CreateTable(
                name: "redirect_usage",
                schema: "pages",
                columns: table => new
                {
                    redirect_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hits = table.Column<long>(type: "bigint", nullable: false),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redirect_usage", x => x.redirect_id);
                    table.ForeignKey(
                        name: "fk_redirect_usage_redirects_redirect_id",
                        column: x => x.redirect_id,
                        principalSchema: "pages",
                        principalTable: "redirects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 017 research SE-23: every site with pages starts at generation 1. Sites created
            // later get their row on their first sitemap bump (SitemapGeneration).
            migrationBuilder.Sql(
                """
                INSERT INTO pages.sitemap_state (site_id, generation)
                SELECT DISTINCT site_id, 1 FROM pages.pages
                ON CONFLICT (site_id) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_redirect_changes_redirect",
                schema: "pages",
                table: "redirect_changes",
                columns: new[] { "site_id", "redirect_id", "at_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_redirect_changes_site_time",
                schema: "pages",
                table: "redirect_changes",
                columns: new[] { "site_id", "at_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_redirects_target_page",
                schema: "pages",
                table: "redirects",
                columns: new[] { "site_id", "target_page_id" },
                filter: "target_page_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_redirects_target_path",
                schema: "pages",
                table: "redirects",
                columns: new[] { "site_id", "target_path" },
                filter: "target_path IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_redirects_source",
                schema: "pages",
                table: "redirects",
                columns: new[] { "site_id", "match_kind", "source_path", "source_query" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "redirect_changes",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "redirect_usage",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "sitemap_state",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "redirects",
                schema: "pages");

            migrationBuilder.DropColumn(
                name: "redirect_to_page_id",
                schema: "pages",
                table: "publish_schedules");
        }
    }
}
