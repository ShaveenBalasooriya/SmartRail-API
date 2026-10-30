using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "fact_order_seq",
                schema: "operations");

            migrationBuilder.CreateTable(
                name: "status_changes",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: true),
                    delay_minutes = table.Column<int>(type: "integer", nullable: true),
                    new_platform_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_platform_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corrects_change_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fact_order = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('operations.fact_order_seq')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_status_changes", x => x.id);
                    table.CheckConstraint("CK_status_changes_change_type", "change_type IN ('delay','cancellation','termination','platform_change','correction')");
                    table.CheckConstraint("CK_status_changes_provenance", "provenance IN ('automatically_published')");
                    table.ForeignKey(
                        name: "FK_status_changes_journey_live_states_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "operations",
                        principalTable: "journey_live_states",
                        principalColumn: "journey_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_status_changes_status_changes_corrects_change_id",
                        column: x => x.corrects_change_id,
                        principalSchema: "operations",
                        principalTable: "status_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_status_changes_corrects_change_id",
                schema: "operations",
                table: "status_changes",
                column: "corrects_change_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_status_changes_journey_id_fact_order",
                schema: "operations",
                table: "status_changes",
                columns: new[] { "journey_id", "fact_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "status_changes",
                schema: "operations");

            migrationBuilder.DropSequence(
                name: "fact_order_seq",
                schema: "operations");
        }
    }
}
