using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.RailNetwork.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLineColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every line needs a colour, so the column starts with the neutral value for any row an
            // older seed already created; the two seeded lines then take their own colours below.
            migrationBuilder.AddColumn<string>(
                name: "color",
                schema: "network",
                table: "lines",
                type: "text",
                nullable: false,
                defaultValue: "#64748b");

            migrationBuilder.Sql(
                """
                UPDATE network.lines SET color = '#1d4ed8' WHERE code = 'COA';
                UPDATE network.lines SET color = '#0f766e' WHERE code = 'MAI';
                """);

            // The default was only there to fill existing rows; new rows always carry a colour.
            migrationBuilder.Sql("ALTER TABLE network.lines ALTER COLUMN color DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                schema: "network",
                table: "lines");
        }
    }
}
