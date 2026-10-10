using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations.Risk;

/// <inheritdoc />
public partial class AddRiskFlagDisposition : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Disposition",
            schema: "risk",
            table: "RiskFlags",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DispositionedAt",
            schema: "risk",
            table: "RiskFlags",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReviewerNotes",
            schema: "risk",
            table: "RiskFlags",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RiskFlags_Status",
            schema: "risk",
            table: "RiskFlags",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RiskFlags_Status",
            schema: "risk",
            table: "RiskFlags");

        migrationBuilder.DropColumn(
            name: "Disposition",
            schema: "risk",
            table: "RiskFlags");

        migrationBuilder.DropColumn(
            name: "DispositionedAt",
            schema: "risk",
            table: "RiskFlags");

        migrationBuilder.DropColumn(
            name: "ReviewerNotes",
            schema: "risk",
            table: "RiskFlags");
    }
}
