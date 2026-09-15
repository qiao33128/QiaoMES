using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Production.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Quantity",
                schema: "production",
                table: "production_reports",
                newName: "ScrapQuantity");

            migrationBuilder.AddColumn<Guid>(
                name: "BomId",
                schema: "production",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BomVersion",
                schema: "production",
                table: "work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                schema: "production",
                table: "work_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RoutingId",
                schema: "production",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoutingVersion",
                schema: "production",
                table: "work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefectCode",
                schema: "production",
                table: "production_reports",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefectQuantity",
                schema: "production",
                table: "production_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GoodQuantity",
                schema: "production",
                table: "production_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OperatorId",
                schema: "production",
                table: "production_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remark",
                schema: "production",
                table: "production_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkOrderOperationId",
                schema: "production",
                table: "production_reports",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "work_order_operations",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    StandardSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsQualityGate = table.Column<bool>(type: "boolean", nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    GoodQuantity = table.Column<int>(type: "integer", nullable: false),
                    DefectQuantity = table.Column<int>(type: "integer", nullable: false),
                    ScrapQuantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkOrderId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_operations_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "production",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_work_order_operations_work_orders_WorkOrderId1",
                        column: x => x.WorkOrderId1,
                        principalSchema: "production",
                        principalTable: "work_orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_ProductId_Status",
                schema: "production",
                table: "work_orders",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_production_reports_WorkOrderOperationId",
                schema: "production",
                table: "production_reports",
                column: "WorkOrderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_operations_WorkOrderId",
                schema: "production",
                table: "work_order_operations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_operations_WorkOrderId_Sequence",
                schema: "production",
                table: "work_order_operations",
                columns: new[] { "WorkOrderId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_operations_WorkOrderId1",
                schema: "production",
                table: "work_order_operations",
                column: "WorkOrderId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_operations",
                schema: "production");

            migrationBuilder.DropIndex(
                name: "IX_work_orders_ProductId_Status",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "IX_production_reports_WorkOrderOperationId",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "BomId",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "BomVersion",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ProductId",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "RoutingId",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "RoutingVersion",
                schema: "production",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "DefectCode",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "DefectQuantity",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "GoodQuantity",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "Remark",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "WorkOrderOperationId",
                schema: "production",
                table: "production_reports");

            migrationBuilder.RenameColumn(
                name: "ScrapQuantity",
                schema: "production",
                table: "production_reports",
                newName: "Quantity");
        }
    }
}
