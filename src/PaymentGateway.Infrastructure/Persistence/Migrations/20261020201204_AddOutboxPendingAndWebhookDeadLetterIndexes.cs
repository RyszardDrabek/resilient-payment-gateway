using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOutboxPendingAndWebhookDeadLetterIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Pending",
            schema: "pay",
            table: "OutboxMessages",
            column: "CreatedAt",
            filter: "\"ProcessedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantWebhookDeadLetters_MerchantId_FailedAt",
            schema: "pay",
            table: "MerchantWebhookDeadLetters",
            columns: new[] { "MerchantId", "FailedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Pending",
            schema: "pay",
            table: "OutboxMessages");

        migrationBuilder.DropIndex(
            name: "IX_MerchantWebhookDeadLetters_MerchantId_FailedAt",
            schema: "pay",
            table: "MerchantWebhookDeadLetters");
    }
}
