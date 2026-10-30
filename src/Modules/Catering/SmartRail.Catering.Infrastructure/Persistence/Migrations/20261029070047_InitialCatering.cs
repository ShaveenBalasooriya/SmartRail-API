using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// EF Core generates one array per index; the analyzer's static-field suggestion does not apply to
// a migration that runs once.
#pragma warning disable CA1861

namespace SmartRail.Catering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catering");

            migrationBuilder.CreateTable(
                name: "providers",
                schema: "catering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "catering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    callback_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_subscriptions_providers_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "catering",
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "catering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_response_status = table.Column<int>(type: "integer", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.id);
                    table.CheckConstraint("CK_webhook_deliveries_event_type", "event_type IN ('delay_published','cancellation_published','termination_published','platform_changed','correction_published')");
                    table.CheckConstraint("CK_webhook_deliveries_status", "status IN ('pending','delivered','failed','abandoned')");
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_webhook_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "catering",
                        principalTable: "webhook_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_subscription_id_event_id",
                schema: "catering",
                table: "webhook_deliveries",
                columns: new[] { "subscription_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscriptions_provider_id",
                schema: "catering",
                table: "webhook_subscriptions",
                column: "provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "catering");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "catering");

            migrationBuilder.DropTable(
                name: "providers",
                schema: "catering");
        }
    }
}
