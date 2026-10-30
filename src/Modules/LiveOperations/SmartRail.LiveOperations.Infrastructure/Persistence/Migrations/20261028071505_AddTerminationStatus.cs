using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_journey_events_event_type",
                schema: "operations",
                table: "journey_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_journey_events_event_type",
                schema: "operations",
                table: "journey_events",
                sql: "event_type IN ('departure_logged','delay_published','cancellation_published','termination_published','platform_changed','correction_published','journey_completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_journey_events_event_type",
                schema: "operations",
                table: "journey_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_journey_events_event_type",
                schema: "operations",
                table: "journey_events",
                sql: "event_type IN ('departure_logged','delay_published','cancellation_published','platform_changed','correction_published','journey_completed')");
        }
    }
}
