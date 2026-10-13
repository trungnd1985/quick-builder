using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRedirect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "redirect_old_address",
                schema: "cms",
                table: "item_schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "redirect_to_page_id",
                schema: "cms",
                table: "item_schedules",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "redirect_old_address",
                schema: "cms",
                table: "item_schedules");

            migrationBuilder.DropColumn(
                name: "redirect_to_page_id",
                schema: "cms",
                table: "item_schedules");
        }
    }
}
