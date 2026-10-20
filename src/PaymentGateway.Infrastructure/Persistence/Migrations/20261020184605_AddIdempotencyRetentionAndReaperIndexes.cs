using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIdempotencyRetentionAndReaperIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_ExpiresAt",
            schema: "pay",
            table: "IdempotencyRecords",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_InFlight_LockedUntil",
            schema: "pay",
            table: "IdempotencyRecords",
            column: "LockedUntil",
            filter: "\"Status\" = 'InFlight' AND \"LockedUntil\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_IdempotencyRecords_ExpiresAt",
            schema: "pay",
            table: "IdempotencyRecords");

        migrationBuilder.DropIndex(
            name: "IX_IdempotencyRecords_InFlight_LockedUntil",
            schema: "pay",
            table: "IdempotencyRecords");
    }
}
