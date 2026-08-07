using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Production.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "production");

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    CompletedQuantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkCenter = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "production_reports",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_reports_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "production",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_production_reports_WorkOrderId",
                schema: "production",
                table: "production_reports",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_OrderNumber",
                schema: "production",
                table: "work_orders",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_reports",
                schema: "production");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "production");
        }
    }
}
