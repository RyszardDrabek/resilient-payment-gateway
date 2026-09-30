using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIdempotencyRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IdempotencyRecords",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CommandType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                ResponsePayload = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_CommandType_Key",
            schema: "pay",
            table: "IdempotencyRecords",
            columns: new[] { "CommandType", "Key" },
            unique: true,
            filter: "\"CommandType\" = 'Authorize'");

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_PaymentId_CommandType_Key",
            schema: "pay",
            table: "IdempotencyRecords",
            columns: new[] { "PaymentId", "CommandType", "Key" },
            unique: true,
            filter: "\"CommandType\" != 'Authorize'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IdempotencyRecords",
            schema: "pay");
    }
}
