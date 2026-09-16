using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyShiftMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_shift_metrics",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShiftName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlannedHours = table.Column<double>(type: "double precision", nullable: false),
                    TotalSn = table.Column<int>(type: "integer", nullable: false),
                    CompletedSn = table.Column<int>(type: "integer", nullable: false),
                    ScrappedSn = table.Column<int>(type: "integer", nullable: false),
                    InProcessSn = table.Column<int>(type: "integer", nullable: false),
                    OnHoldSn = table.Column<int>(type: "integer", nullable: false),
                    InspectionTotal = table.Column<int>(type: "integer", nullable: false),
                    InspectionPassed = table.Column<int>(type: "integer", nullable: false),
                    InspectionFailed = table.Column<int>(type: "integer", nullable: false),
                    InspectionConcessioned = table.Column<int>(type: "integer", nullable: false),
                    DefectQuantity = table.Column<int>(type: "integer", nullable: false),
                    TheoreticalSeconds = table.Column<long>(type: "bigint", nullable: false),
                    ActualSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DowntimeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DowntimeCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_shift_metrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_shift_metrics_ProductionDate",
                schema: "reporting",
                table: "daily_shift_metrics",
                column: "ProductionDate");

            migrationBuilder.CreateIndex(
                name: "IX_daily_shift_metrics_ProductionDate_ShiftCode_LineName",
                schema: "reporting",
                table: "daily_shift_metrics",
                columns: new[] { "ProductionDate", "ShiftCode", "LineName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_shift_metrics",
                schema: "reporting");
        }
    }
}
