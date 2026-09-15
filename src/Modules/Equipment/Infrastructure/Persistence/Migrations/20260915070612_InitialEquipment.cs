using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Equipment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "equipment");

            migrationBuilder.CreateTable(
                name: "andon_calls",
                schema: "equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquipmentCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkCenterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CallerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CalledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResponderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Escalated = table.Column<bool>(type: "boolean", nullable: false),
                    EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_andon_calls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "equipment_number_sequences",
                schema: "equipment",
                columns: table => new
                {
                    sequence_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_number_sequences", x => x.sequence_key);
                });

            migrationBuilder.CreateTable(
                name: "equipments",
                schema: "equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    LineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DownReasonCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalDownSeconds = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "equipment_maintenance_records",
                schema: "equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    AbnormalDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExecutorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_maintenance_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_equipment_maintenance_records_equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "equipment",
                        principalTable: "equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipment_status_logs",
                schema: "equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_status_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_equipment_status_logs_equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "equipment",
                        principalTable: "equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_CalledAt",
                schema: "equipment",
                table: "andon_calls",
                column: "CalledAt");

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_CallNumber",
                schema: "equipment",
                table: "andon_calls",
                column: "CallNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_EquipmentId",
                schema: "equipment",
                table: "andon_calls",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_Status_Level",
                schema: "equipment",
                table: "andon_calls",
                columns: new[] { "Status", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_maintenance_records_EquipmentId",
                schema: "equipment",
                table: "equipment_maintenance_records",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_maintenance_records_ExecutedAt",
                schema: "equipment",
                table: "equipment_maintenance_records",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_status_logs_ChangedAt",
                schema: "equipment",
                table: "equipment_status_logs",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_status_logs_EquipmentId",
                schema: "equipment",
                table: "equipment_status_logs",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_equipments_Code",
                schema: "equipment",
                table: "equipments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_equipments_Status_IsActive",
                schema: "equipment",
                table: "equipments",
                columns: new[] { "Status", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_equipments_WorkCenterId",
                schema: "equipment",
                table: "equipments",
                column: "WorkCenterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "andon_calls",
                schema: "equipment");

            migrationBuilder.DropTable(
                name: "equipment_maintenance_records",
                schema: "equipment");

            migrationBuilder.DropTable(
                name: "equipment_number_sequences",
                schema: "equipment");

            migrationBuilder.DropTable(
                name: "equipment_status_logs",
                schema: "equipment");

            migrationBuilder.DropTable(
                name: "equipments",
                schema: "equipment");
        }
    }
}
