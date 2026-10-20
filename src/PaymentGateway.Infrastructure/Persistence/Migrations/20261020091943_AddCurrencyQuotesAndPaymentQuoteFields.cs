using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCurrencyQuotesAndPaymentQuoteFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ExchangeRate",
            schema: "pay",
            table: "Payments",
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "OriginalAmount",
            schema: "pay",
            table: "Payments",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OriginalCurrency",
            schema: "pay",
            table: "Payments",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "QuoteId",
            schema: "pay",
            table: "Payments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CurrencyQuotes",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                BaseCurrency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                BaseAmount = table.Column<long>(type: "bigint", nullable: false),
                TargetCurrency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                TargetAmount = table.Column<long>(type: "bigint", nullable: false),
                SpotRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                SpreadBps = table.Column<int>(type: "integer", nullable: false),
                ExchangeRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ConsumedByPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CurrencyQuotes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyQuotes_ExpiresAt",
            schema: "pay",
            table: "CurrencyQuotes",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyQuotes_Status",
            schema: "pay",
            table: "CurrencyQuotes",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CurrencyQuotes",
            schema: "pay");

        migrationBuilder.DropColumn(
            name: "ExchangeRate",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "OriginalAmount",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "OriginalCurrency",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "QuoteId",
            schema: "pay",
            table: "Payments");
    }
}
