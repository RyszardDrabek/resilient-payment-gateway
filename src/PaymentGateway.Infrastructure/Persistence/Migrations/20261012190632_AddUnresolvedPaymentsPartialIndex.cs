using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUnresolvedPaymentsPartialIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Payments_Unresolved",
            schema: "pay",
            table: "Payments",
            columns: new[] { "CreatedAt", "SettlementChannel" },
            filter: "\"State\" IN ('Pending', 'Unknown')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Payments_Unresolved",
            schema: "pay",
            table: "Payments");
    }
}
