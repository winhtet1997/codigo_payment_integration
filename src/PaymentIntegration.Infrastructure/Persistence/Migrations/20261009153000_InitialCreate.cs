using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;

#nullable disable

namespace PaymentIntegration.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates customer, product, transaction, and customer_card.
/// Applied automatically on startup by Database.Migrate().
/// </summary>
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "customer",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                email = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer", x => x.id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "product",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                payment_type = table.Column<string>(type: "enum('OneTime','Recurring')", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product", x => x.id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "customer_card",
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                customer_id = table.Column<int>(type: "int", nullable: false),
                customer_token = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                masked_card_no = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                created_at = table.Column<DateTime>(type: "datetime", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_card", x => x.id);
                table.ForeignKey(
                    name: "FK_customer_card_customer_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customer",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "transaction",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                customer_id = table.Column<int>(type: "int", nullable: false),
                product_id = table.Column<int>(type: "int", nullable: false),
                invoice_no = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                idempotency_key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                request_fingerprint = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                currency = table.Column<string>(type: "char(3)", maxLength: 3, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                status = table.Column<string>(type: "enum('Pending','Succeeded','Failed')", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                payment_channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                masked_card_no = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                provider_ref = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                resp_code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                resp_desc = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                payment_token = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                checkout_response_json = table.Column<string>(type: "text", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                paid_at = table.Column<DateTime>(type: "datetime", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transaction", x => x.id);
                table.ForeignKey(
                    name: "FK_transaction_customer_customer_id",
                    column: x => x.customer_id,
                    principalTable: "customer",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_transaction_product_product_id",
                    column: x => x.product_id,
                    principalTable: "product",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_customer_card_customer_id_customer_token",
            table: "customer_card",
            columns: new[] { "customer_id", "customer_token" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transaction_invoice_no",
            table: "transaction",
            column: "invoice_no",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transaction_idempotency_key",
            table: "transaction",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_transaction_customer_id",
            table: "transaction",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_transaction_product_id",
            table: "transaction",
            column: "product_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "transaction");
        migrationBuilder.DropTable(name: "customer_card");
        migrationBuilder.DropTable(name: "product");
        migrationBuilder.DropTable(name: "customer");
    }
}
