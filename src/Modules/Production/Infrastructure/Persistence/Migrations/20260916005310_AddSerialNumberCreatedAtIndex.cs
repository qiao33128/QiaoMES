using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiaoMES.Production.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialNumberCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_CreatedAt_Status",
                schema: "production",
                table: "serial_numbers",
                columns: new[] { "CreatedAt", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_serial_numbers_CreatedAt_Status",
                schema: "production",
                table: "serial_numbers");
        }
    }
}
