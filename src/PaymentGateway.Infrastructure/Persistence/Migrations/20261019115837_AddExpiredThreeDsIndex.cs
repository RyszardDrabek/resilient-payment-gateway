using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExpiredThreeDsIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Payments_RequiresAction_ExpiresAt",
            schema: "pay",
            table: "Payments",
            column: "ThreeDsAction_ExpiresAt",
            filter: "\"State\" = 'RequiresAction' AND \"ThreeDsAction_ExpiresAt\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Payments_RequiresAction_ExpiresAt",
            schema: "pay",
            table: "Payments");
    }
}
