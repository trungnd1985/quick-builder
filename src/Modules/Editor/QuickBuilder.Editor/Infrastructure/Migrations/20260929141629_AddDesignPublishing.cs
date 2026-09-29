using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Editor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDesignPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "design_publications",
                schema: "editor",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    live_number = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    published_from_token_version = table.Column<long>(type: "bigint", nullable: true),
                    live_since_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    live_by = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_publications", x => x.site_id);
                    table.CheckConstraint("ck_design_publications_numbers", "live_number >= 1 AND next_number > live_number");
                });

            migrationBuilder.CreateTable(
                name: "design_versions",
                schema: "editor",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    restored_from_number = table.Column<int>(type: "integer", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_versions", x => new { x.site_id, x.number });
                    table.CheckConstraint("ck_design_versions_kind", "kind IN ('publish', 'rollback')");
                    table.CheckConstraint("ck_design_versions_number", "number >= 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "design_publications",
                schema: "editor");

            migrationBuilder.DropTable(
                name: "design_versions",
                schema: "editor");
        }
    }
}
