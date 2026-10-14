using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMerchantWebhookDeadLetters : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MerchantWebhookDeadLetters",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                MerchantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DestinationUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                DeliveryAttempts = table.Column<int>(type: "integer", nullable: false),
                LastErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReplayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MerchantWebhookDeadLetters", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MerchantWebhookDeadLetters_FailedAt",
            schema: "pay",
            table: "MerchantWebhookDeadLetters",
            column: "FailedAt");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantWebhookDeadLetters_MerchantId_Status",
            schema: "pay",
            table: "MerchantWebhookDeadLetters",
            columns: new[] { "MerchantId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MerchantWebhookDeadLetters",
            schema: "pay");
    }
}
