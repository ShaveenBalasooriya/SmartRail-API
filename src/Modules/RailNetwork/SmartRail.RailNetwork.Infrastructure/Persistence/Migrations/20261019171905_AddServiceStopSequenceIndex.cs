using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceStopSequenceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_stops_service_id",
                schema: "network",
                table: "service_stops");

            migrationBuilder.CreateIndex(
                name: "IX_service_stops_service_id_sequence",
                schema: "network",
                table: "service_stops",
                columns: new[] { "service_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_stops_service_id_sequence",
                schema: "network",
                table: "service_stops");

            migrationBuilder.CreateIndex(
                name: "IX_service_stops_service_id",
                schema: "network",
                table: "service_stops",
                column: "service_id");
        }
    }
}
