using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "calendar_days",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_days", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    LineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shifts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_days_Date",
                schema: "reporting",
                table: "calendar_days",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shifts_Code",
                schema: "reporting",
                table: "shifts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shifts_LineName_IsActive",
                schema: "reporting",
                table: "shifts",
                columns: new[] { "LineName", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_days",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "shifts",
                schema: "reporting");
        }
    }
}
