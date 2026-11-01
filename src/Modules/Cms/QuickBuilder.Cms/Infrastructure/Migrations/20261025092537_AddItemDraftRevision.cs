using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickBuilder.Cms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDraftRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "draft_change_kind",
                schema: "cms",
                table: "items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "member");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "draft_changed_at_utc",
                schema: "cms",
                table: "items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<Guid>(
                name: "draft_changed_by",
                schema: "cms",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "draft_revision",
                schema: "cms",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // 023 data-model §3.1: every existing item starts with its draft as last changed by
            // its last writer. Seeded items have no writer (Guid.Empty), so nobody is named.
            migrationBuilder.Sql(
                """
                UPDATE cms.items
                SET draft_revision = revision,
                    draft_changed_by = NULLIF(updated_by, '00000000-0000-0000-0000-000000000000'),
                    draft_changed_at_utc = updated_at_utc,
                    draft_change_kind = 'member';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_draft_change_kind",
                schema: "cms",
                table: "items",
                sql: "(draft_change_kind = 'member') OR (draft_change_kind = 'schema_change' AND draft_changed_by IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_draft_revision",
                schema: "cms",
                table: "items",
                sql: "draft_revision >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_items_draft_change_kind",
                schema: "cms",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_items_draft_revision",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "draft_change_kind",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "draft_changed_at_utc",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "draft_changed_by",
                schema: "cms",
                table: "items");

            migrationBuilder.DropColumn(
                name: "draft_revision",
                schema: "cms",
                table: "items");
        }
    }
}
