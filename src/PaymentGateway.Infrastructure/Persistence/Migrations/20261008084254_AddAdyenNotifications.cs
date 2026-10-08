using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAdyenNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdyenNotifications",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PspReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                OriginalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MerchantAccountCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                MerchantReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EventCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EventDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AmountValue = table.Column<long>(type: "bigint", nullable: false),
                AmountCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Success = table.Column<bool>(type: "boolean", nullable: false),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                CorrelatedPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdyenNotifications", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdyenNotifications_CorrelatedPaymentId",
            schema: "pay",
            table: "AdyenNotifications",
            column: "CorrelatedPaymentId");

        migrationBuilder.CreateIndex(
            name: "IX_AdyenNotifications_MerchantReference",
            schema: "pay",
            table: "AdyenNotifications",
            column: "MerchantReference");

        migrationBuilder.CreateIndex(
            name: "IX_AdyenNotifications_PspReference_EventCode",
            schema: "pay",
            table: "AdyenNotifications",
            columns: new[] { "PspReference", "EventCode" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AdyenNotifications",
            schema: "pay");
    }
}
