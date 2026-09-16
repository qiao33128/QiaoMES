using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Assistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assistant");

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "assistant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LlmBaseUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LlmModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LlmApiKeyProtected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LlmTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRows = table.Column<int>(type: "integer", nullable: false),
                    QueryTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRepairAttempts = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settings_UpdatedAt",
                schema: "assistant",
                table: "settings",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings",
                schema: "assistant");
        }
    }
}
