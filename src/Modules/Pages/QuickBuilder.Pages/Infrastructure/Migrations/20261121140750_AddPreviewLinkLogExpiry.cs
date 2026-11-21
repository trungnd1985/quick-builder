using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewLinkLogExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at_utc",
                schema: "pages",
                table: "publish_log",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expires_at_utc",
                schema: "pages",
                table: "publish_log");
        }
    }
}
