using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddReconciliationAndPayouts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FeeSchedules",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MerchantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Currency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                FixedFeeMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                PercentageFeeBps = table.Column<int>(type: "integer", nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FeeSchedules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PayoutBatches",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MerchantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                GrossAmount = table.Column<long>(type: "bigint", nullable: false),
                FeeAmount = table.Column<long>(type: "bigint", nullable: false),
                NetAmount = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PayoutBatches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ReconciliationRecords",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ChannelReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                GatewayAmount = table.Column<long>(type: "bigint", nullable: false),
                AcquirerAmount = table.Column<long>(type: "bigint", nullable: false),
                DiscrepancyType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReconciliationRecords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PayoutItems",
            schema: "pay",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PayoutBatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                GrossAmount = table.Column<long>(type: "bigint", nullable: false),
                FeeAmount = table.Column<long>(type: "bigint", nullable: false),
                NetAmount = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PayoutItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_PayoutItems_PayoutBatches_PayoutBatchId",
                    column: x => x.PayoutBatchId,
                    principalSchema: "pay",
                    principalTable: "PayoutBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FeeSchedules_MerchantId_Currency_EffectiveFrom",
            schema: "pay",
            table: "FeeSchedules",
            columns: new[] { "MerchantId", "Currency", "EffectiveFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_PayoutBatches_MerchantId_PeriodEnd",
            schema: "pay",
            table: "PayoutBatches",
            columns: new[] { "MerchantId", "PeriodEnd" });

        migrationBuilder.CreateIndex(
            name: "IX_PayoutBatches_Status",
            schema: "pay",
            table: "PayoutBatches",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_PayoutItems_PaymentId",
            schema: "pay",
            table: "PayoutItems",
            column: "PaymentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PayoutItems_PayoutBatchId",
            schema: "pay",
            table: "PayoutItems",
            column: "PayoutBatchId");

        migrationBuilder.CreateIndex(
            name: "IX_ReconciliationRecords_PaymentId",
            schema: "pay",
            table: "ReconciliationRecords",
            column: "PaymentId");

        migrationBuilder.CreateIndex(
            name: "IX_ReconciliationRecords_ReconciledAt",
            schema: "pay",
            table: "ReconciliationRecords",
            column: "ReconciledAt");

        migrationBuilder.CreateIndex(
            name: "IX_ReconciliationRecords_Status",
            schema: "pay",
            table: "ReconciliationRecords",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FeeSchedules",
            schema: "pay");

        migrationBuilder.DropTable(
            name: "PayoutItems",
            schema: "pay");

        migrationBuilder.DropTable(
            name: "ReconciliationRecords",
            schema: "pay");

        migrationBuilder.DropTable(
            name: "PayoutBatches",
            schema: "pay");
    }
}
