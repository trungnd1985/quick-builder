using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRenderStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_published_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "render_stamp",
                schema: "cms",
                table: "items",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_published_slug",
                schema: "cms",
                table: "items",
                columns: new[] { "collection_id", "published_slug" },
                filter: "published_slug IS NOT NULL");

            // 016 research TB-18: items published before this release get their first publish
            // time from the oldest version kept for them, or from their current publish when
            // none is kept. Re-running it changes nothing.
            migrationBuilder.Sql("""
                UPDATE cms.items AS i
                SET first_published_at_utc = COALESCE(
                    (SELECT min(v.published_at_utc) FROM cms.item_versions AS v WHERE v.item_id = i.id),
                    i.published_at_utc)
                WHERE i.first_published_at_utc IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_items_collection_id_published_slug",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "first_published_at_utc",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "render_stamp",
                schema: "cms",
                table: "items");
        }
    }
}
