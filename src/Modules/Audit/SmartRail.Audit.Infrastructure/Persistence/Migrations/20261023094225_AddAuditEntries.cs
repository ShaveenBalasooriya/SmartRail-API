using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chain_position = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: true),
                    target_id = table.Column<string>(type: "text", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    related_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_entries_entries_related_entry_id",
                        column: x => x.related_entry_id,
                        principalSchema: "audit",
                        principalTable: "entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entries_chain_position",
                schema: "audit",
                table: "entries",
                column: "chain_position",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entries_related_entry_id",
                schema: "audit",
                table: "entries",
                column: "related_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_entries_source_event_id",
                schema: "audit",
                table: "entries",
                column: "source_event_id",
                unique: true);

            // The record is append-only at the database itself (design D4): the application's role
            // owns the table, so a revoked grant would not stop it, but a BEFORE UPDATE OR DELETE
            // trigger refuses both from every session.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION audit.reject_audit_entry_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.entries is append-only: % is refused', TG_OP;
                END;
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER entries_append_only
                BEFORE UPDATE OR DELETE ON audit.entries
                FOR EACH ROW
                EXECUTE FUNCTION audit.reject_audit_entry_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER entries_append_only ON audit.entries;");
            migrationBuilder.Sql("DROP FUNCTION audit.reject_audit_entry_change();");

            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");
        }
    }
}
