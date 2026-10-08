using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "attention",
                schema: "cms",
                table: "items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "index_version",
                schema: "cms",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "live_version",
                schema: "cms",
                table: "items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "cms",
                table: "items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "next_version_number",
                schema: "cms",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_slug",
                schema: "cms",
                table: "items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "cms",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                schema: "cms",
                table: "items",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "cms",
                table: "items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "cms",
                table: "items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "schema_revision",
                schema: "cms",
                table: "collections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "item_history",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: true),
                    schedule_action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_history_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_media",
                schema: "cms",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    in_draft = table.Column<bool>(type: "boolean", nullable: false),
                    in_live = table.Column<bool>(type: "boolean", nullable: false),
                    in_scheduled = table.Column<bool>(type: "boolean", nullable: false),
                    in_history = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_media", x => new { x.item_id, x.media_id });
                    table.ForeignKey(
                        name: "fk_item_media_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_references",
                schema: "cms",
                columns: table => new
                {
                    source_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    target_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    in_draft = table.Column<bool>(type: "boolean", nullable: false),
                    in_published = table.Column<bool>(type: "boolean", nullable: false),
                    in_scheduled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_references", x => new { x.source_item_id, x.field_id, x.target_item_id });
                    table.CheckConstraint("ck_item_references_not_self", "source_item_id <> target_item_id");
                    table.CheckConstraint("ck_item_references_somewhere", "in_draft OR in_published OR in_scheduled");
                    table.ForeignKey(
                        name: "fk_item_references_items_source_item_id",
                        column: x => x.source_item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_references_items_target_item_id",
                        column: x => x.target_item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "item_schedules",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    captured_values = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    captured_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_schedules", x => x.id);
                    table.CheckConstraint("ck_item_schedules_action", "action IN ('publish', 'unpublish')");
                    table.CheckConstraint("ck_item_schedules_state", "state IN ('pending', 'done', 'cancelled', 'failed')");
                    table.ForeignKey(
                        name: "fk_item_schedules_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_slugs",
                schema: "cms",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_slugs", x => new { x.collection_id, x.slug });
                    table.ForeignKey(
                        name: "fk_item_slugs_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "cms",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_slugs_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_versions",
                schema: "cms",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    values = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    restored_from = table.Column<int>(type: "integer", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_versions", x => new { x.item_id, x.number });
                    table.CheckConstraint("ck_item_versions_kind", "kind IN ('publish', 'scheduled', 'rollback')");
                    table.ForeignKey(
                        name: "fk_item_versions_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "cms",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 015 research IT-22: items stored by 014 (tests and demos only) get what #15 needs
            // before the constraints below apply: a placeholder slug, a status, revision 1, and
            // for published ones a version 1 that is live. ItemIndexBackfillService then derives
            // name, search text, attention and the item tables, and replaces the placeholder
            // slugs from names. Safe to re-run: every statement only fills what is still empty.
            migrationBuilder.Sql("""
                UPDATE cms.collections SET schema_revision = revision WHERE schema_revision = 0;

                UPDATE cms.items SET
                    slug = 'item-' || right(replace(id::text, '-', ''), 12),
                    revision = 1,
                    status = CASE WHEN published IS NULL THEN 'draft' ELSE 'published' END,
                    published_slug = CASE WHEN published IS NULL THEN NULL ELSE 'item-' || right(replace(id::text, '-', ''), 12) END,
                    next_version_number = CASE WHEN published IS NULL THEN 1 ELSE 2 END,
                    live_version = CASE WHEN published IS NULL THEN NULL ELSE 1 END,
                    published_at_utc = CASE WHEN published IS NULL THEN NULL ELSE updated_at_utc END,
                    published_by = CASE WHEN published IS NULL THEN NULL ELSE '00000000-0000-0000-0000-000000000000'::uuid END
                WHERE slug = '';

                INSERT INTO cms.item_versions (item_id, number, site_id, "values", slug, kind, restored_from, published_by, published_at_utc)
                SELECT id, 1, site_id, published, published_slug, 'publish', NULL, '00000000-0000-0000-0000-000000000000'::uuid, updated_at_utc
                FROM cms.items
                WHERE published IS NOT NULL
                ON CONFLICT DO NOTHING;

                INSERT INTO cms.item_slugs (collection_id, slug, item_id, site_id)
                SELECT collection_id, slug, id, site_id FROM cms.items
                ON CONFLICT DO NOTHING;
                """);

            // Research IT-14, analysis I2: names may be long, so they are sorted on their first
            // 256 characters, and this expression index (which EF Core cannot model) serves it.
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_items_collection_id_name ON cms.items (collection_id, left(name, 256));");

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_attention",
                schema: "cms",
                table: "items",
                column: "collection_id",
                filter: "attention IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_slug",
                schema: "cms",
                table: "items",
                columns: new[] { "collection_id", "slug" });

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_status",
                schema: "cms",
                table: "items",
                columns: new[] { "collection_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_updated_at_utc_id",
                schema: "cms",
                table: "items",
                columns: new[] { "collection_id", "updated_at_utc", "id" },
                descending: new[] { false, true, false });

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_published_slug",
                schema: "cms",
                table: "items",
                sql: "(published IS NULL) = (published_slug IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_revision",
                schema: "cms",
                table: "items",
                sql: "revision >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_status",
                schema: "cms",
                table: "items",
                sql: "status IN ('draft', 'published', 'changed')");

            migrationBuilder.CreateIndex(
                name: "ix_item_history_item_id_id",
                schema: "cms",
                table: "item_history",
                columns: new[] { "item_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_media_site_id_media_id",
                schema: "cms",
                table: "item_media",
                columns: new[] { "site_id", "media_id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_references_target_item_id",
                schema: "cms",
                table: "item_references",
                column: "target_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_schedules_item_id_action",
                schema: "cms",
                table: "item_schedules",
                columns: new[] { "item_id", "action" },
                unique: true,
                filter: "state = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_item_slugs_item_id",
                schema: "cms",
                table: "item_slugs",
                column: "item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_history",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "item_media",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "item_references",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "item_schedules",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "item_slugs",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "item_versions",
                schema: "cms");

            migrationBuilder.DropIndex(
                name: "ix_items_collection_id_attention",
                schema: "cms",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_collection_id_slug",
                schema: "cms",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_collection_id_status",
                schema: "cms",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_collection_id_updated_at_utc_id",
                schema: "cms",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_published_slug",
                schema: "cms",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_revision",
                schema: "cms",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_status",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "attention",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "index_version",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "live_version",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "next_version_number",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "published_at_utc",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "published_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "published_slug",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "search_text",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "schema_revision",
                schema: "cms",
                table: "collections");

            // citext stays: the Pages, Sites and Identity schemas use it too.
        }
    }
}
