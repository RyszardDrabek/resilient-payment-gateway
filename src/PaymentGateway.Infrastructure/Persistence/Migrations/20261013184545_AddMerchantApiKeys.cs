using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMerchantApiKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MerchantApiKeys",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MerchantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Prefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                KeyHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Salt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Scopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MerchantApiKeys", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MerchantApiKeys_MerchantId_IsRevoked",
            schema: "pay",
            table: "MerchantApiKeys",
            columns: new[] { "MerchantId", "IsRevoked" });

        migrationBuilder.CreateIndex(
            name: "IX_MerchantApiKeys_Prefix",
            schema: "pay",
            table: "MerchantApiKeys",
            column: "Prefix");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MerchantApiKeys",
            schema: "pay");
    }
}
