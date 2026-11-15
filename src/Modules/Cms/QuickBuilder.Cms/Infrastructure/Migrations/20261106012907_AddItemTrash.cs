using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trashed_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_had_schedule",
                schema: "cms",
                table: "items",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trashed_was_live",
                schema: "cms",
                table: "items",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_trash",
                schema: "cms",
                table: "items",
                columns: new[] { "site_id", "trashed_at_utc", "id" },
                descending: new[] { false, true, false },
                filter: "trashed_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_trash",
                schema: "cms",
                table: "items",
                sql: "(trashed_at_utc IS NULL) = (trashed_by IS NULL) AND (trashed_at_utc IS NULL) = (trashed_was_live IS NULL) AND (trashed_at_utc IS NULL) = (trashed_had_schedule IS NULL) AND (trashed_at_utc IS NULL OR published IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_items_trash",
                schema: "cms",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_trash",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_at_utc",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_had_schedule",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_was_live",
                schema: "cms",
                table: "items");
        }
    }
}
