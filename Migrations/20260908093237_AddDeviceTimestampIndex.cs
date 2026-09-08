using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLogAnalyzer.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Logs_DeviceId_Timestamp",
                table: "Logs",
                columns: new[] { "DeviceId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Logs_DeviceId_Timestamp",
                table: "Logs");
        }
    }
}
