using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Sites.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "redirect_other_hosts",
                schema: "sites",
                table: "sites",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "search_default_image",
                schema: "sites",
                table: "sites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "search_discouraged",
                schema: "sites",
                table: "sites",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "search_org_logo",
                schema: "sites",
                table: "sites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_org_name",
                schema: "sites",
                table: "sites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_robots_rules",
                schema: "sites",
                table: "sites",
                type: "character varying(10240)",
                maxLength: 10240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_title_suffix",
                schema: "sites",
                table: "sites",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "search_updated_by",
                schema: "sites",
                table: "sites",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "redirect_other_hosts",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_default_image",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_discouraged",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_org_logo",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_org_name",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_robots_rules",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_title_suffix",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "search_updated_by",
                schema: "sites",
                table: "sites");
        }
    }
}
