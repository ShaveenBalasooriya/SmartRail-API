using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartRail.Catering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueWebhookSubscriptionPerProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_subscriptions_provider_id",
                schema: "catering",
                table: "webhook_subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscriptions_provider_id",
                schema: "catering",
                table: "webhook_subscriptions",
                column: "provider_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_subscriptions_provider_id",
                schema: "catering",
                table: "webhook_subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscriptions_provider_id",
                schema: "catering",
                table: "webhook_subscriptions",
                column: "provider_id");
        }
    }
}
