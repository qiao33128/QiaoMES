using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Quality.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "quality");

            migrationBuilder.CreateTable(
                name: "defect_codes",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkOrderOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterialCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    AqlLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AcceptedLimit = table.Column<int>(type: "integer", nullable: false),
                    RejectedLimit = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Conclusion = table.Column<int>(type: "integer", nullable: false),
                    DefectQuantity = table.Column<int>(type: "integer", nullable: false),
                    InspectorId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nonconformances",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NonconformanceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefectCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DefectDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Disposition = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NeedReinspect = table.Column<bool>(type: "boolean", nullable: false),
                    HandlerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReinspectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nonconformances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_number_sequences",
                schema: "quality",
                columns: table => new
                {
                    sequence_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_number_sequences", x => x.sequence_key);
                });

            migrationBuilder.CreateTable(
                name: "inspection_items",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Standard = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LowerLimit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    IsKeyItem = table.Column<bool>(type: "boolean", nullable: false),
                    MeasuredValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    IsQualified = table.Column<bool>(type: "boolean", nullable: true),
                    DefectCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InspectionId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_items_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "quality",
                        principalTable: "inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inspection_items_inspections_InspectionId1",
                        column: x => x.InspectionId1,
                        principalSchema: "quality",
                        principalTable: "inspections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "repair_records",
                schema: "quality",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NonconformanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RepairerId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NonconformanceId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_repair_records_nonconformances_NonconformanceId",
                        column: x => x.NonconformanceId,
                        principalSchema: "quality",
                        principalTable: "nonconformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_repair_records_nonconformances_NonconformanceId1",
                        column: x => x.NonconformanceId1,
                        principalSchema: "quality",
                        principalTable: "nonconformances",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_defect_codes_Category_IsActive",
                schema: "quality",
                table: "defect_codes",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_defect_codes_Code",
                schema: "quality",
                table: "defect_codes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_InspectionId",
                schema: "quality",
                table: "inspection_items",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_InspectionId1",
                schema: "quality",
                table: "inspection_items",
                column: "InspectionId1");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_CreatedAt",
                schema: "quality",
                table: "inspections",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_InspectionNumber",
                schema: "quality",
                table: "inspections",
                column: "InspectionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspections_Sn",
                schema: "quality",
                table: "inspections",
                column: "Sn");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_Type_Status",
                schema: "quality",
                table: "inspections",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inspections_WorkOrderId",
                schema: "quality",
                table: "inspections",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_nonconformances_InspectionId",
                schema: "quality",
                table: "nonconformances",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_nonconformances_NonconformanceNumber",
                schema: "quality",
                table: "nonconformances",
                column: "NonconformanceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nonconformances_Sn",
                schema: "quality",
                table: "nonconformances",
                column: "Sn");

            migrationBuilder.CreateIndex(
                name: "IX_nonconformances_Status",
                schema: "quality",
                table: "nonconformances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_repair_records_NonconformanceId",
                schema: "quality",
                table: "repair_records",
                column: "NonconformanceId");

            migrationBuilder.CreateIndex(
                name: "IX_repair_records_NonconformanceId1",
                schema: "quality",
                table: "repair_records",
                column: "NonconformanceId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "defect_codes",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "inspection_items",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "quality_number_sequences",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "repair_records",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "inspections",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "nonconformances",
                schema: "quality");
        }
    }
}
