using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations.Risk;

/// <inheritdoc />
public partial class InitialRiskSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "risk");

        migrationBuilder.CreateTable(
            name: "PartyRiskContexts",
            schema: "risk",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Amount = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PartyRiskContexts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RiskFlags",
            schema: "risk",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                RaisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RiskFlags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RiskVerdicts",
            schema: "risk",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PartyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                FlagId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RiskVerdicts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PartyRiskContexts_PartyId",
            schema: "risk",
            table: "PartyRiskContexts",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_RiskFlags_PartyId",
            schema: "risk",
            table: "RiskFlags",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_RiskFlags_PaymentId",
            schema: "risk",
            table: "RiskFlags",
            column: "PaymentId");

        migrationBuilder.CreateIndex(
            name: "IX_RiskVerdicts_EventId",
            schema: "risk",
            table: "RiskVerdicts",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RiskVerdicts_PaymentId",
            schema: "risk",
            table: "RiskVerdicts",
            column: "PaymentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PartyRiskContexts",
            schema: "risk");

        migrationBuilder.DropTable(
            name: "RiskFlags",
            schema: "risk");

        migrationBuilder.DropTable(
            name: "RiskVerdicts",
            schema: "risk");
    }
}
