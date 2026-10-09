using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ExtendPaymentCurrencyMaxLength : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Currency",
            schema: "pay",
            table: "Payments",
            type: "character varying(12)",
            maxLength: 12,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(3)",
            oldMaxLength: 3);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Currency",
            schema: "pay",
            table: "Payments",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(12)",
            oldMaxLength: 12);
    }
}
