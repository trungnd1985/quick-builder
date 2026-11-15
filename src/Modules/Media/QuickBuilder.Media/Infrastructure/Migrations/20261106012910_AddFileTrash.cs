using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trashed_at_utc",
                schema: "media",
                table: "media_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_by",
                schema: "media",
                table: "media_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_folder_id",
                schema: "media",
                table: "media_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_trash",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "trashed_at_utc", "id" },
                descending: new[] { false, true, false },
                filter: "trashed_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_files_trash",
                schema: "media",
                table: "media_files",
                sql: "(trashed_at_utc IS NULL) = (trashed_by IS NULL) AND (trashed_at_utc IS NOT NULL OR trashed_folder_id IS NULL) AND (trashed_at_utc IS NULL OR folder_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_media_files_trash",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_files_trash",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "trashed_at_utc",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "trashed_by",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "trashed_folder_id",
                schema: "media",
                table: "media_files");
        }
    }
}
