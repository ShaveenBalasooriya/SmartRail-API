using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartureLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "departure_logs",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guard_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delay_cause = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fact_order = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('operations.fact_order_seq')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departure_logs", x => x.id);
                    table.CheckConstraint("CK_departure_logs_delay_cause", "delay_cause IN ('signalling','rolling_stock','crew','passenger_boarding','infrastructure','weather','other')");
                    table.ForeignKey(
                        name: "FK_departure_logs_journey_live_states_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "operations",
                        principalTable: "journey_live_states",
                        principalColumn: "journey_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_departure_logs_journey_id_stop_sequence",
                schema: "operations",
                table: "departure_logs",
                columns: new[] { "journey_id", "stop_sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "departure_logs",
                schema: "operations");
        }
    }
}
