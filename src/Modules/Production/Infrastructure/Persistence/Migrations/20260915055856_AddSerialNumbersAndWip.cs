using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Production.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialNumbersAndWip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualSeconds",
                schema: "production",
                table: "work_order_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "EquipmentId",
                schema: "production",
                table: "production_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportType",
                schema: "production",
                table: "production_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkedSeconds",
                schema: "production",
                table: "production_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentOperationTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentOperationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastCompletedOperationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serial_numbers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wip_trackings",
                schema: "production",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TrackedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wip_trackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wip_trackings_serial_numbers_SerialNumberId",
                        column: x => x.SerialNumberId,
                        principalSchema: "production",
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_Sn",
                schema: "production",
                table: "serial_numbers",
                column: "Sn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_WorkOrderId_Status",
                schema: "production",
                table: "serial_numbers",
                columns: new[] { "WorkOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_wip_trackings_SerialNumberId",
                schema: "production",
                table: "wip_trackings",
                column: "SerialNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_wip_trackings_WorkOrderId_TrackedAt",
                schema: "production",
                table: "wip_trackings",
                columns: new[] { "WorkOrderId", "TrackedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wip_trackings",
                schema: "production");

            migrationBuilder.DropTable(
                name: "serial_numbers",
                schema: "production");

            migrationBuilder.DropColumn(
                name: "ActualSeconds",
                schema: "production",
                table: "work_order_operations");

            migrationBuilder.DropColumn(
                name: "EquipmentId",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "ReportType",
                schema: "production",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "WorkedSeconds",
                schema: "production",
                table: "production_reports");
        }
    }
}
