using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopNGo.StockService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemVersionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "inventory_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "inventory_items");
        }
    }
}
