using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Infrastructure.Migrations
{
    public partial class RemapPaymentStatusValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Orders" SET "PaymentStatus" = CASE "PaymentStatus"
                    WHEN 0 THEN 0
                    WHEN 1 THEN 0
                    WHEN 2 THEN 1
                    WHEN 3 THEN 2
                    WHEN 4 THEN 3
                    ELSE 0
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Orders" SET "PaymentStatus" = CASE "PaymentStatus"
                    WHEN 0 THEN 0
                    WHEN 1 THEN 2
                    WHEN 2 THEN 3
                    WHEN 3 THEN 4
                    ELSE 0
                END;
                """);
        }
    }
}
