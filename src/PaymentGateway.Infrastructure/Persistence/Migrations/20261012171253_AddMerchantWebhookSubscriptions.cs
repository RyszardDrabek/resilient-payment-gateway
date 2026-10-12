using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMerchantWebhookSubscriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MerchantWebhookSubscriptions",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EndpointUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                SecretKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MerchantWebhookSubscriptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MerchantWebhookSubscriptions_PartyId",
            schema: "pay",
            table: "MerchantWebhookSubscriptions",
            column: "PartyId",
            unique: true,
            filter: "\"IsActive\" = true");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MerchantWebhookSubscriptions",
            schema: "pay");
    }
}
