using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBomAndRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boms",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "routings",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bom_items",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BomId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LossRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bom_items_boms_BomId",
                        column: x => x.BomId,
                        principalSchema: "masterdata",
                        principalTable: "boms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routing_steps",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    StandardSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsQualityGate = table.Column<bool>(type: "boolean", nullable: false),
                    RoutingId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_routing_steps_routings_RoutingId",
                        column: x => x.RoutingId,
                        principalSchema: "masterdata",
                        principalTable: "routings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_routing_steps_routings_RoutingId1",
                        column: x => x.RoutingId1,
                        principalSchema: "masterdata",
                        principalTable: "routings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_items_BomId",
                schema: "masterdata",
                table: "bom_items",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_items_MaterialId",
                schema: "masterdata",
                table: "bom_items",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_boms_ProductId_Version",
                schema: "masterdata",
                table: "boms",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routing_steps_RoutingId",
                schema: "masterdata",
                table: "routing_steps",
                column: "RoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_routing_steps_RoutingId_Sequence",
                schema: "masterdata",
                table: "routing_steps",
                columns: new[] { "RoutingId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_routing_steps_RoutingId1",
                schema: "masterdata",
                table: "routing_steps",
                column: "RoutingId1");

            migrationBuilder.CreateIndex(
                name: "IX_routings_ProductId_Version",
                schema: "masterdata",
                table: "routings",
                columns: new[] { "ProductId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_items",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "routing_steps",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "boms",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "routings",
                schema: "masterdata");
        }
    }
}
