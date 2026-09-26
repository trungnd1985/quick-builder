using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Sites.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SiteSettingsAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_language",
                schema: "sites",
                table: "sites",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "primary_host_name",
                schema: "sites",
                table: "sites",
                type: "citext",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            // 009 research S-16: every existing site takes its earliest host name as
            // primary, ties broken by id so a re-run picks the same one. Deliberately updates
            // every site at once; UpgradeFrom003Tests is its dedicated test.
            migrationBuilder.Sql(
                """
                UPDATE sites.sites AS s
                SET primary_host_name = p.host_name
                FROM (
                    SELECT DISTINCT ON (site_id) site_id, host_name
                    FROM sites.site_host_names
                    ORDER BY site_id, created_at_utc, id
                ) AS p
                WHERE s.id = p.site_id;
                """);

            migrationBuilder.AddColumn<string>(
                name: "scheme",
                schema: "sites",
                table: "sites",
                type: "text",
                nullable: false,
                defaultValue: "https");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "sites",
                table: "sites",
                type: "text",
                nullable: false,
                defaultValue: "enabled");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "sites",
                table: "sites",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sites_scheme",
                schema: "sites",
                table: "sites",
                sql: "scheme IN ('https', 'http')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sites_status",
                schema: "sites",
                table: "sites",
                sql: "status IN ('enabled', 'disabled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sites_scheme",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sites_status",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "default_language",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "primary_host_name",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "scheme",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "sites",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "sites",
                table: "sites");
        }
    }
}
