using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCopyOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_id",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "copied_from_saved_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_copy_origin",
                schema: "cms",
                table: "items",
                sql: "(copied_from_id IS NULL) = (copied_from_saved_at_utc IS NULL) AND (copied_from_id IS NULL) = (copied_by IS NULL) AND (copied_from_id IS NULL) = (copied_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_items_copy_origin",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "copied_at_utc",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "copied_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "copied_from_id",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "copied_from_saved_at_utc",
                schema: "cms",
                table: "items");
        }
    }
}
