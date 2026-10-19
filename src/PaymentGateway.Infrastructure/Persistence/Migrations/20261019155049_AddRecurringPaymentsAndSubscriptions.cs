using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRecurringPaymentsAndSubscriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentMethodId",
            schema: "pay",
            table: "Payments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SubscriptionId",
            schema: "pay",
            table: "Payments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TransactionOrigin",
            schema: "pay",
            table: "Payments",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Cit");

        migrationBuilder.CreateTable(
            name: "PaymentMethods",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SettlementChannel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CardBrand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MaskedPan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExpiryMonth = table.Column<int>(type: "integer", nullable: false),
                ExpiryYear = table.Column<int>(type: "integer", nullable: false),
                Token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentMethods", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Subscriptions",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PaymentMethodId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Amount = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                Interval = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                NextBillingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FailureCount = table.Column<int>(type: "integer", nullable: false),
                LastBilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Subscriptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentMethods_PartyId_Status",
            schema: "pay",
            table: "PaymentMethods",
            columns: new[] { "PartyId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_PartyId",
            schema: "pay",
            table: "Subscriptions",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_State_NextBillingAt",
            schema: "pay",
            table: "Subscriptions",
            columns: new[] { "State", "NextBillingAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PaymentMethods",
            schema: "pay");

        migrationBuilder.DropTable(
            name: "Subscriptions",
            schema: "pay");

        migrationBuilder.DropColumn(
            name: "PaymentMethodId",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "SubscriptionId",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "TransactionOrigin",
            schema: "pay",
            table: "Payments");
    }
}
