using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCurrencyQuotesActiveExpiresAtIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CurrencyQuotes_ExpiresAt",
            schema: "pay",
            table: "CurrencyQuotes");

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyQuotes_Active_ExpiresAt",
            schema: "pay",
            table: "CurrencyQuotes",
            column: "ExpiresAt",
            filter: "\"Status\" = 'Active'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CurrencyQuotes_Active_ExpiresAt",
            schema: "pay",
            table: "CurrencyQuotes");

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyQuotes_ExpiresAt",
            schema: "pay",
            table: "CurrencyQuotes",
            column: "ExpiresAt");
    }
}
