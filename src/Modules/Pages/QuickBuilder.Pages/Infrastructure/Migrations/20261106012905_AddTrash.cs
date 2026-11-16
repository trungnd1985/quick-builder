using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_pages_site_path",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ux_components_site_name",
                schema: "pages",
                table: "components");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trashed_at_utc",
                schema: "pages",
                table: "pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_by",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_had_schedule",
                schema: "pages",
                table: "pages",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_parent_id",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trashed_path",
                schema: "pages",
                table: "pages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "trashed_position",
                schema: "pages",
                table: "pages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_template_collection_id",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_was_home",
                schema: "pages",
                table: "pages",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trashed_at_utc",
                schema: "pages",
                table: "components",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_by",
                schema: "pages",
                table: "components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_had_schedule",
                schema: "pages",
                table: "components",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_was_live",
                schema: "pages",
                table: "components",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_trash",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "trashed_at_utc", "id" },
                descending: new[] { false, true, false },
                filter: "trashed_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pages_site_path",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "path" },
                unique: true,
                filter: "trashed_at_utc IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pages_trash",
                schema: "pages",
                table: "pages",
                sql: "(trashed_at_utc IS NULL) = (trashed_by IS NULL) AND (trashed_at_utc IS NULL) = (trashed_path IS NULL) AND (trashed_at_utc IS NULL) = (trashed_position IS NULL) AND (trashed_at_utc IS NULL) = (trashed_was_home IS NULL) AND (trashed_at_utc IS NULL) = (trashed_had_schedule IS NULL) AND (trashed_at_utc IS NOT NULL OR (trashed_parent_id IS NULL AND trashed_template_collection_id IS NULL)) AND (trashed_at_utc IS NULL OR (parent_id IS NULL AND NOT is_home AND template_collection_id IS NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_components_trash",
                schema: "pages",
                table: "components",
                columns: new[] { "site_id", "trashed_at_utc", "id" },
                descending: new[] { false, true, false },
                filter: "trashed_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_components_site_name",
                schema: "pages",
                table: "components",
                columns: new[] { "site_id", "name" },
                unique: true,
                filter: "trashed_at_utc IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_components_trash",
                schema: "pages",
                table: "components",
                sql: "(trashed_at_utc IS NULL) = (trashed_by IS NULL) AND (trashed_at_utc IS NULL) = (trashed_was_live IS NULL) AND (trashed_at_utc IS NULL) = (trashed_had_schedule IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pages_trash",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ux_pages_site_path",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pages_trash",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_components_trash",
                schema: "pages",
                table: "components");

            migrationBuilder.DropIndex(
                name: "ux_components_site_name",
                schema: "pages",
                table: "components");

            migrationBuilder.DropCheckConstraint(
                name: "ck_components_trash",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "trashed_at_utc",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_by",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_had_schedule",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_parent_id",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_path",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_position",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_template_collection_id",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_was_home",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "trashed_at_utc",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "trashed_by",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "trashed_had_schedule",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "trashed_was_live",
                schema: "pages",
                table: "components");

            migrationBuilder.CreateIndex(
                name: "ux_pages_site_path",
                schema: "pages",
                table: "pages",
                columns: new[] { "site_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_components_site_name",
                schema: "pages",
                table: "components",
                columns: new[] { "site_id", "name" },
                unique: true);
        }
    }
}
