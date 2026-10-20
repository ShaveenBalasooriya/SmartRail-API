using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveJourneyState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateSequence(
                name: "journey_events_stream_position_seq",
                schema: "operations");

            migrationBuilder.CreateTable(
                name: "journey_live_states",
                schema: "operations",
                columns: table => new
                {
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    guard_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_status = table.Column<string>(type: "text", nullable: false),
                    delay_minutes = table.Column<int>(type: "integer", nullable: true),
                    last_departed_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_live_states", x => x.journey_id);
                });

            migrationBuilder.CreateTable(
                name: "journey_events",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    stream_position = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('operations.journey_events_stream_position_seq')"),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    status_change_id = table.Column<Guid>(type: "uuid", nullable: true),
                    departure_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_events", x => x.id);
                    table.CheckConstraint("CK_journey_events_event_type", "event_type IN ('departure_logged','delay_published','cancellation_published','platform_changed','correction_published','journey_completed')");
                    table.ForeignKey(
                        name: "FK_journey_events_journey_live_states_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "operations",
                        principalTable: "journey_live_states",
                        principalColumn: "journey_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journey_stop_live_states",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_calling = table.Column<bool>(type: "boolean", nullable: false),
                    scheduled_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_departure = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_stop_live_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_journey_stop_live_states_journey_live_states_journey_id",
                        column: x => x.journey_id,
                        principalSchema: "operations",
                        principalTable: "journey_live_states",
                        principalColumn: "journey_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_journey_events_journey_id_sequence_number",
                schema: "operations",
                table: "journey_events",
                columns: new[] { "journey_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journey_events_stream_position",
                schema: "operations",
                table: "journey_events",
                column: "stream_position");

            migrationBuilder.CreateIndex(
                name: "IX_journey_live_states_service_date",
                schema: "operations",
                table: "journey_live_states",
                column: "service_date");

            migrationBuilder.CreateIndex(
                name: "IX_journey_stop_live_states_journey_id_sequence",
                schema: "operations",
                table: "journey_stop_live_states",
                columns: new[] { "journey_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journey_events",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "journey_stop_live_states",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "journey_live_states",
                schema: "operations");

            migrationBuilder.DropSequence(
                name: "journey_events_stream_position_seq",
                schema: "operations");
        }
    }
}
