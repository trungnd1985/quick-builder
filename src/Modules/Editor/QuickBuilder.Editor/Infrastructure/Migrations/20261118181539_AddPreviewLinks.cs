using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Editor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preview_link_failures",
                schema: "editor",
                columns: table => new
                {
                    client_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    window_kind = table.Column<short>(type: "smallint", nullable: false),
                    window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preview_link_failures", x => new { x.client_key, x.window_kind, x.window_start_utc });
                    table.CheckConstraint("ck_preview_link_failures_kind", "window_kind IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "preview_links",
                schema: "editor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: true),
                    sample_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preview_links", x => x.id);
                    table.CheckConstraint("ck_preview_links_expiry", "expires_at_utc > created_at_utc AND expires_at_utc <= created_at_utc + interval '30 days'");
                    table.CheckConstraint("ck_preview_links_kind", "document_kind IN ('page', 'item')");
                    table.CheckConstraint("ck_preview_links_revoked", "(revoked_at_utc IS NULL) = (revoked_by IS NULL)");
                    table.CheckConstraint("ck_preview_links_sample", "sample_item_id IS NULL OR document_kind = 'page'");
                    table.CheckConstraint("ck_preview_links_secret_hash", "octet_length(secret_hash) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "ix_preview_link_failures_sweep",
                schema: "editor",
                table: "preview_link_failures",
                column: "window_start_utc");

            migrationBuilder.CreateIndex(
                name: "ix_preview_links_document",
                schema: "editor",
                table: "preview_links",
                columns: new[] { "site_id", "document_kind", "document_id" },
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_preview_links_site_active",
                schema: "editor",
                table: "preview_links",
                columns: new[] { "site_id", "expires_at_utc" },
                filter: "revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_preview_links_sweep",
                schema: "editor",
                table: "preview_links",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_preview_links_secret_hash",
                schema: "editor",
                table: "preview_links",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preview_link_failures",
                schema: "editor");

            migrationBuilder.DropTable(
                name: "preview_links",
                schema: "editor");
        }
    }
}
