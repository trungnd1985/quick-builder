using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCopyOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_at_utc",
                schema: "pages",
                table: "pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_by",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_id",
                schema: "pages",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_from_saved_at_utc",
                schema: "pages",
                table: "pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_at_utc",
                schema: "pages",
                table: "components",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_by",
                schema: "pages",
                table: "components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_id",
                schema: "pages",
                table: "components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_from_saved_at_utc",
                schema: "pages",
                table: "components",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pages_copy_origin",
                schema: "pages",
                table: "pages",
                sql: "(copied_from_id IS NULL) = (copied_from_saved_at_utc IS NULL) AND (copied_from_id IS NULL) = (copied_by IS NULL) AND (copied_from_id IS NULL) = (copied_at_utc IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_components_copy_origin",
                schema: "pages",
                table: "components",
                sql: "(copied_from_id IS NULL) = (copied_from_saved_at_utc IS NULL) AND (copied_from_id IS NULL) = (copied_by IS NULL) AND (copied_from_id IS NULL) = (copied_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pages_copy_origin",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_components_copy_origin",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "copied_at_utc",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "copied_by",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "copied_from_id",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "copied_from_saved_at_utc",
                schema: "pages",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "copied_at_utc",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "copied_by",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "copied_from_id",
                schema: "pages",
                table: "components");

            migrationBuilder.DropColumn(
                name: "copied_from_saved_at_utc",
                schema: "pages",
                table: "components");
        }
    }
}
