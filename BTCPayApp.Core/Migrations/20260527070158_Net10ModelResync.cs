using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class Net10ModelResync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelAliases_LightningChannels_ChannelId",
                table: "ChannelAliases");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Value",
                table: "Settings",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<long>(
                name: "Value",
                table: "LightningPayments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                table: "LightningPayments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentRequest",
                table: "LightningPayments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Data",
                table: "LightningChannels",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "ChannelAliases",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelAliases_LightningChannels_ChannelId",
                table: "ChannelAliases",
                column: "ChannelId",
                principalTable: "LightningChannels",
                principalColumn: "Id");

            // The AlterColumn calls above each trigger an EF SQLite table rebuild, which re-creates
            // the Laraue update-triggers from the current model. We therefore do NOT re-issue the
            // trigger DDL here: the writable_schema DELETE hack does not take effect inside EF's
            // migration transaction, so an explicit CREATE would collide with the rebuild's copy.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelAliases_LightningChannels_ChannelId",
                table: "ChannelAliases");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Value",
                table: "Settings",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Value",
                table: "LightningPayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Secret",
                table: "LightningPayments",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentRequest",
                table: "LightningPayments",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "Data",
                table: "LightningChannels",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "ChannelAliases",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelAliases_LightningChannels_ChannelId",
                table: "ChannelAliases",
                column: "ChannelId",
                principalTable: "LightningChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // See the note in Up(): the AlterColumn table rebuilds re-create the Laraue triggers
            // from the model, so we do not re-issue the trigger DDL here.
        }
    }
}
