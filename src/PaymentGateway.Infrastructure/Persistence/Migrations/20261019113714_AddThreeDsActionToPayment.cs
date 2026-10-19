using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddThreeDsActionToPayment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ThreeDsAction_ActionType",
            schema: "pay",
            table: "Payments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ThreeDsAction_ExpiresAt",
            schema: "pay",
            table: "Payments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ThreeDsAction_TargetUrl",
            schema: "pay",
            table: "Payments",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ThreeDsAction_Token",
            schema: "pay",
            table: "Payments",
            type: "character varying(4096)",
            maxLength: 4096,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ThreeDsAction_ActionType",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "ThreeDsAction_ExpiresAt",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "ThreeDsAction_TargetUrl",
            schema: "pay",
            table: "Payments");

        migrationBuilder.DropColumn(
            name: "ThreeDsAction_Token",
            schema: "pay",
            table: "Payments");
    }
}
