using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.AddColumn<string[]>(
                name: "page_param_names",
                schema: "pages",
                table: "publications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<Guid>(
                name: "template_collection_id",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collection_uses",
                schema: "pages",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_ids = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_uses", x => new { x.site_id, x.subject_kind, x.subject_id, x.state, x.collection_id });
                    table.CheckConstraint("ck_collection_uses_state", "state IN ('draft', 'live', 'scheduled')");
                    table.CheckConstraint("ck_collection_uses_subject_kind", "subject_kind IN ('page', 'component')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_pages_site_template_collection",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "template_collection_id" },
                unique: true,
                filter: "template_collection_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "target_kind IN ('component', 'page', 'media', 'collection', 'template')");

            migrationBuilder.CreateIndex(
                name: "ix_collection_uses_collection",
                schema: "pages",
                table: "collection_uses",
                columns: new[] { "site_id", "collection_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_uses",
                schema: "pages");

            migrationBuilder.DropIndex(
                name: "ux_pages_site_template_collection",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies");

            migrationBuilder.DropColumn(
                name: "page_param_names",
                schema: "pages",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "template_collection_id",
                schema: "pages",
                table: "pages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_live_dependencies_target_kind",
                schema: "pages",
                table: "live_dependencies",
                sql: "target_kind IN ('component', 'page', 'media')");
        }
    }
}
