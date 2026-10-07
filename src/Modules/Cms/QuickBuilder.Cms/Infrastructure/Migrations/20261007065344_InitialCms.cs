using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cms");

            migrationBuilder.CreateTable(
                name: "collections",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    next_field_number = table.Column<int>(type: "integer", nullable: false),
                    next_option_number = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collections", x => x.id);
                    table.CheckConstraint("ck_collections_counters", "next_field_number >= 1 AND next_option_number >= 1");
                    table.CheckConstraint("ck_collections_revision", "revision >= 1");
                    table.CheckConstraint("ck_collections_schema_version", "schema_version = 1");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "cms",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.event_id, x.handler_name });
                });

            migrationBuilder.CreateTable(
                name: "collection_references",
                schema: "cms",
                columns: table => new
                {
                    source_collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_collection_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_references", x => new { x.source_collection_id, x.field_id });
                    table.CheckConstraint("ck_collection_references_not_self", "source_collection_id <> target_collection_id");
                    table.ForeignKey(
                        name: "fk_collection_references_collections_source_collection_id",
                        column: x => x.source_collection_id,
                        principalSchema: "cms",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_collection_references_collections_target_collection_id",
                        column: x => x.target_collection_id,
                        principalSchema: "cms",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    published = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "cms",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schema_changes",
                schema: "cms",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schema_changes", x => new { x.collection_id, x.revision });
                    table.ForeignKey(
                        name: "fk_schema_changes_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "cms",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_references_target_collection_id",
                schema: "cms",
                table: "collection_references",
                column: "target_collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_collections_site_id_name",
                schema: "cms",
                table: "collections",
                columns: new[] { "site_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_collections_site_id_slug",
                schema: "cms",
                table: "collections",
                columns: new[] { "site_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_collection_id_id",
                schema: "cms",
                table: "items",
                columns: new[] { "collection_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_completed",
                schema: "cms",
                table: "outbox_messages",
                column: "completed_at_utc",
                filter: "completed_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "cms",
                table: "outbox_messages",
                column: "available_at_utc",
                filter: "completed_at_utc IS NULL AND failed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_references",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "items",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "schema_changes",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "collections",
                schema: "cms");
        }
    }
}
