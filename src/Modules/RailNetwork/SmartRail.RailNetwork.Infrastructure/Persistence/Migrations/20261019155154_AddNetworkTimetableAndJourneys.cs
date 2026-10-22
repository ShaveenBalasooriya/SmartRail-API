using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkTimetableAndJourneys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "network");

            migrationBuilder.CreateTable(
                name: "lines",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    guard_staff_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.id);
                    table.ForeignKey(
                        name: "FK_services_lines_line_id",
                        column: x => x.line_id,
                        principalSchema: "network",
                        principalTable: "lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "line_stations",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_stations", x => x.id);
                    table.ForeignKey(
                        name: "FK_line_stations_lines_line_id",
                        column: x => x.line_id,
                        principalSchema: "network",
                        principalTable: "lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_line_stations_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platforms",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platforms", x => x.id);
                    table.ForeignKey(
                        name: "FK_platforms_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "journeys",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_departure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journeys", x => x.id);
                    table.ForeignKey(
                        name: "FK_journeys_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "network",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_stops",
                schema: "network",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    is_calling = table.Column<bool>(type: "boolean", nullable: false),
                    scheduled_arrival = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    scheduled_departure = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_stops", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_stops_platforms_platform_id",
                        column: x => x.platform_id,
                        principalSchema: "network",
                        principalTable: "platforms",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_service_stops_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "network",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_service_stops_stations_station_id",
                        column: x => x.station_id,
                        principalSchema: "network",
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_journeys_service_id_service_date",
                schema: "network",
                table: "journeys",
                columns: new[] { "service_id", "service_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_line_stations_line_id_sequence",
                schema: "network",
                table: "line_stations",
                columns: new[] { "line_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_line_stations_line_id_station_id",
                schema: "network",
                table: "line_stations",
                columns: new[] { "line_id", "station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_line_stations_station_id",
                schema: "network",
                table: "line_stations",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_lines_code",
                schema: "network",
                table: "lines",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platforms_station_id_number",
                schema: "network",
                table: "platforms",
                columns: new[] { "station_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_stops_platform_id",
                schema: "network",
                table: "service_stops",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_stops_service_id",
                schema: "network",
                table: "service_stops",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "IX_service_stops_station_id",
                schema: "network",
                table: "service_stops",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_services_line_id",
                schema: "network",
                table: "services",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "IX_stations_code",
                schema: "network",
                table: "stations",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journeys",
                schema: "network");

            migrationBuilder.DropTable(
                name: "line_stations",
                schema: "network");

            migrationBuilder.DropTable(
                name: "service_stops",
                schema: "network");

            migrationBuilder.DropTable(
                name: "platforms",
                schema: "network");

            migrationBuilder.DropTable(
                name: "services",
                schema: "network");

            migrationBuilder.DropTable(
                name: "stations",
                schema: "network");

            migrationBuilder.DropTable(
                name: "lines",
                schema: "network");
        }
    }
}
