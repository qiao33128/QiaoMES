using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Quality.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                schema: "quality",
                table: "inspections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "material_lots",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IqcInspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IqcInspectionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InspectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_lots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sn_material_consumptions",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkOrderOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EquipmentCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sn_material_consumptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_lots_LotNumber",
                schema: "quality",
                table: "material_lots",
                column: "LotNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_lots_MaterialCode_Status",
                schema: "quality",
                table: "material_lots",
                columns: new[] { "MaterialCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_material_lots_ReceivedAt",
                schema: "quality",
                table: "material_lots",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_sn_material_consumptions_BoundAt",
                schema: "quality",
                table: "sn_material_consumptions",
                column: "BoundAt");

            migrationBuilder.CreateIndex(
                name: "IX_sn_material_consumptions_LotNumber",
                schema: "quality",
                table: "sn_material_consumptions",
                column: "LotNumber");

            migrationBuilder.CreateIndex(
                name: "IX_sn_material_consumptions_Sn_LotNumber_MaterialCode",
                schema: "quality",
                table: "sn_material_consumptions",
                columns: new[] { "Sn", "LotNumber", "MaterialCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_lots",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "sn_material_consumptions",
                schema: "quality");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                schema: "quality",
                table: "inspections");
        }
    }
}
