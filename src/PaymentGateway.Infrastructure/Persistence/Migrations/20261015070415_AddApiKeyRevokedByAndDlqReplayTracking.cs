using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddApiKeyRevokedByAndDlqReplayTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "LastReplayStatusCode",
            schema: "pay",
            table: "MerchantWebhookDeadLetters",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReplayAttemptCount",
            schema: "pay",
            table: "MerchantWebhookDeadLetters",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "RevokedBy",
            schema: "pay",
            table: "MerchantApiKeys",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastReplayStatusCode",
            schema: "pay",
            table: "MerchantWebhookDeadLetters");

        migrationBuilder.DropColumn(
            name: "ReplayAttemptCount",
            schema: "pay",
            table: "MerchantWebhookDeadLetters");

        migrationBuilder.DropColumn(
            name: "RevokedBy",
            schema: "pay",
            table: "MerchantApiKeys");
    }
}
