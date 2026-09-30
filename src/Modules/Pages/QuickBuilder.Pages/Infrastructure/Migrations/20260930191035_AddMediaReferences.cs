using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Pages.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "draft_media_references",
                schema: "pages",
                columns: table => new
                {
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_draft_media_references", x => new { x.subject_kind, x.subject_id, x.media_id });
                    table.CheckConstraint("ck_draft_media_references_subject_kind", "subject_kind IN ('page', 'component')");
                });

            migrationBuilder.CreateTable(
                name: "version_media_references",
                schema: "pages",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_version_media_references", x => new { x.version_id, x.media_id });
                    table.ForeignKey(
                        name: "fk_version_media_references_published_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "pages",
                        principalTable: "published_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_draft_media_references_media",
                schema: "pages",
                table: "draft_media_references",
                columns: new[] { "site_id", "media_id" });

            migrationBuilder.CreateIndex(
                name: "ix_version_media_references_media",
                schema: "pages",
                table: "version_media_references",
                columns: new[] { "site_id", "media_id" });

            // 013 research MV-05: usage of every existing draft and version (FR-013). The path is
            // what DependencyExtractor reads: an image node anywhere under the root, its
            // props.media.mediaId, kept only when it is a string shaped like a UUID. No input
            // reaches this SQL; MediaReferenceBackfillTests proves it finds exactly what
            // DependencyExtractor finds on the golden documents, so the two cannot drift.
            const string ImageMediaIds =
                """
                CROSS JOIN LATERAL jsonb_path_query(src.document, 'lax $.root.** ? (@.type == "image").props.media.mediaId') AS found(value)
                WHERE jsonb_typeof(found.value) = 'string'
                  AND (found.value #>> '{}') ~* '^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$'
                """;

            migrationBuilder.Sql(
                $$"""
                INSERT INTO pages.draft_media_references (site_id, subject_kind, subject_id, media_id)
                SELECT DISTINCT src.site_id, 'page', src.page_id, (found.value #>> '{}')::uuid
                FROM pages.page_drafts AS src
                {{ImageMediaIds}}
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql(
                $$"""
                INSERT INTO pages.draft_media_references (site_id, subject_kind, subject_id, media_id)
                SELECT DISTINCT src.site_id, 'component', src.component_id, (found.value #>> '{}')::uuid
                FROM pages.component_drafts AS src
                {{ImageMediaIds}}
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql(
                $$"""
                INSERT INTO pages.version_media_references (site_id, version_id, media_id)
                SELECT DISTINCT src.site_id, src.id, (found.value #>> '{}')::uuid
                FROM pages.published_versions AS src
                {{ImageMediaIds}}
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_media_references",
                schema: "pages");

            migrationBuilder.DropTable(
                name: "version_media_references",
                schema: "pages");
        }
    }
}
