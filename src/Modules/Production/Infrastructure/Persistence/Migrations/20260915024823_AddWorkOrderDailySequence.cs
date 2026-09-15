using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Production.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderDailySequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_daily_sequences",
                schema: "production",
                columns: table => new
                {
                    sequence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_daily_sequences", x => x.sequence_date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_Status_CreatedAt",
                schema: "production",
                table: "work_orders",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_daily_sequences",
                schema: "production");

            migrationBuilder.DropIndex(
                name: "IX_work_orders_Status_CreatedAt",
                schema: "production",
                table: "work_orders");
        }
    }
}
