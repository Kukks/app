using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class NArkModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_Wallet",
                schema: "ark",
                table: "Wallets");

            migrationBuilder.AlterColumn<string>(
                name: "Wallet",
                schema: "ark",
                table: "Wallets",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Wallet",
                schema: "ark",
                table: "Wallets",
                column: "Wallet",
                unique: true,
                filter: "\"Wallet\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_Wallet",
                schema: "ark",
                table: "Wallets");

            migrationBuilder.AlterColumn<string>(
                name: "Wallet",
                schema: "ark",
                table: "Wallets",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Wallet",
                schema: "ark",
                table: "Wallets",
                column: "Wallet",
                unique: true);
        }
    }
}
