using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopNGo.OrderService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationChannel",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "NotificationChannel",
                table: "orders");
        }
    }
}
