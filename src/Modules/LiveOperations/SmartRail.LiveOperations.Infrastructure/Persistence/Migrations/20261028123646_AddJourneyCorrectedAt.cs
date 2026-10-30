using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.LiveOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJourneyCorrectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "corrected_at",
                schema: "operations",
                table: "journey_live_states",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "corrected_at",
                schema: "operations",
                table: "journey_live_states");
        }
    }
}
