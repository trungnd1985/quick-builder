using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alt_text",
                schema: "media",
                table: "media_files",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "content_version",
                schema: "media",
                table: "media_files",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                schema: "media",
                table: "media_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                schema: "media",
                table: "media_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_animated",
                schema: "media",
                table: "media_files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "metadata_state",
                schema: "media",
                table: "media_files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replaced_at_utc",
                schema: "media",
                table: "media_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by",
                schema: "media",
                table: "media_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                schema: "media",
                table: "media_files",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "search_text_state",
                schema: "media",
                table: "media_files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "sort_name",
                schema: "media",
                table: "media_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "variants_state",
                schema: "media",
                table: "media_files",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "width",
                schema: "media",
                table: "media_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "media",
                table: "media_files",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "folder_trees",
                schema: "media",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folder_trees", x => x.site_id);
                });

            migrationBuilder.CreateTable(
                name: "folders",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folders", x => x.id);
                    table.UniqueConstraint("ak_folders_site_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_folders_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_folders_parent",
                        columns: x => new { x.site_id, x.parent_id },
                        principalSchema: "media",
                        principalTable: "folders",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_checks",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    files_checked = table.Column<int>(type: "integer", nullable: false),
                    missing_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_checks", x => x.id);
                    table.CheckConstraint("ck_storage_checks_status", "status IN ('running', 'completed', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "variant_claims",
                schema: "media",
                columns: table => new
                {
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_version = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_claims", x => new { x.file_id, x.content_version, x.width });
                });

            migrationBuilder.CreateTable(
                name: "storage_check_missing",
                schema: "media",
                columns: table => new
                {
                    check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_check_missing", x => new { x.check_id, x.site_id, x.file_id });
                    table.ForeignKey(
                        name: "fk_storage_check_missing_check",
                        column: x => x.check_id,
                        principalSchema: "media",
                        principalTable: "storage_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_folder_name",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "folder_id", "sort_name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_folder_size",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "folder_id", "size_bytes", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_folder_uploaded",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "folder_id", "uploaded_at_utc", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_metadata_pending",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "metadata_state" },
                filter: "metadata_state = 0");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_search_text_provisional",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "search_text_state" },
                filter: "search_text_state = 0");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_site_name",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "sort_name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_site_size",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "size_bytes", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_files_alt_text_images",
                schema: "media",
                table: "media_files",
                sql: "alt_text IS NULL OR content_type LIKE 'image/%'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_files_content_version",
                schema: "media",
                table: "media_files",
                sql: "content_version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_files_dimensions",
                schema: "media",
                table: "media_files",
                sql: "(width IS NULL) = (height IS NULL) AND (width IS NULL OR (width > 0 AND height > 0))");

            migrationBuilder.CreateIndex(
                name: "ux_folders_sibling_name",
                schema: "media",
                table: "folders",
                columns: new[] { "site_id", "parent_id", "name_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_storage_checks_started",
                schema: "media",
                table: "storage_checks",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_storage_checks_running",
                schema: "media",
                table: "storage_checks",
                column: "status",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.AddForeignKey(
                name: "fk_media_files_folder",
                schema: "media",
                table: "media_files",
                columns: new[] { "site_id", "folder_id" },
                principalSchema: "media",
                principalTable: "folders",
                principalColumns: new[] { "site_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // 013 research MV-26: files uploaded before this feature. Images are pending until
            // ImageMetadataBackfillService reads their headers; documents have no dimensions.
            // Their search text is provisional (lower case only): a migration emits SQL and
            // cannot run SearchText's accent folding, so SearchTextBackfillService rewrites it
            // after start-up. Every existing file starts at the top level with no alternative
            // text, and keeps its version-1 address (FR-045).
            migrationBuilder.Sql(
                """
                UPDATE media.media_files
                SET metadata_state = CASE WHEN content_type LIKE 'image/%' THEN 0 ELSE 2 END,
                    search_text = lower(file_name),
                    sort_name = left(lower(file_name), 255),
                    search_text_state = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_media_files_folder",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropTable(
                name: "folder_trees",
                schema: "media");

            migrationBuilder.DropTable(
                name: "folders",
                schema: "media");

            migrationBuilder.DropTable(
                name: "storage_check_missing",
                schema: "media");

            migrationBuilder.DropTable(
                name: "variant_claims",
                schema: "media");

            migrationBuilder.DropTable(
                name: "storage_checks",
                schema: "media");

            migrationBuilder.DropIndex(
                name: "ix_media_files_folder_name",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_folder_size",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_folder_uploaded",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_metadata_pending",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_search_text_provisional",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_site_name",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropIndex(
                name: "ix_media_files_site_size",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_files_alt_text_images",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_files_content_version",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_files_dimensions",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "alt_text",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "content_version",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "folder_id",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "height",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "is_animated",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "metadata_state",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "replaced_at_utc",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "replaced_by",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "search_text",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "search_text_state",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "sort_name",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "variants_state",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "width",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "media",
                table: "media_files");
        }
    }
}
