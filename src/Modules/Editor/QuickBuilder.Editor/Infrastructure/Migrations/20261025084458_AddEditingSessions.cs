using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Editor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEditingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "editing_sessions",
                schema: "editor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    holds_lock = table.Column<bool>(type: "boolean", nullable: false),
                    lock_taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lost_lock_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lost_lock_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lost_lock_reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_editing_sessions", x => x.id);
                    table.CheckConstraint("ck_editing_sessions_collection", "(document_kind = 'item') = (collection_id IS NOT NULL)");
                    table.CheckConstraint("ck_editing_sessions_kind", "document_kind IN ('page', 'component', 'item')");
                    table.CheckConstraint("ck_editing_sessions_lock", "holds_lock = (lock_taken_at IS NOT NULL) AND holds_lock = (last_activity_at IS NOT NULL)");
                    table.CheckConstraint("ck_editing_sessions_lost", "(lost_lock_reason IS NULL AND lost_lock_at IS NULL AND lost_lock_to_user_id IS NULL) OR (lost_lock_reason IN ('takeover', 'moved') AND lost_lock_at IS NOT NULL AND lost_lock_to_user_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "editing_takeovers",
                schema: "editor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stale_for_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_editing_takeovers", x => x.id);
                    table.CheckConstraint("ck_editing_takeovers_kind", "document_kind IN ('page', 'component', 'item')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_editing_sessions_document",
                schema: "editor",
                table: "editing_sessions",
                columns: new[] { "site_id", "document_kind", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_editing_sessions_last_seen_at",
                schema: "editor",
                table: "editing_sessions",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_editing_sessions_holder",
                schema: "editor",
                table: "editing_sessions",
                columns: new[] { "site_id", "document_kind", "document_id" },
                unique: true,
                filter: "holds_lock");

            migrationBuilder.CreateIndex(
                name: "ix_editing_takeovers_site_id_taken_at",
                schema: "editor",
                table: "editing_takeovers",
                columns: new[] { "site_id", "taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "editing_sessions",
                schema: "editor");

            migrationBuilder.DropTable(
                name: "editing_takeovers",
                schema: "editor");
        }
    }
}
