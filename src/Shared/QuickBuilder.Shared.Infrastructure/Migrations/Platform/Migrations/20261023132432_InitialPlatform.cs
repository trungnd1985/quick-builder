using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "database_version",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    recorded_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    upgrading_to = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    upgrading_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_database_version", x => x.id);
                    table.CheckConstraint("ck_database_version_singleton", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "database_version",
                schema: "platform");
        }
    }
}
