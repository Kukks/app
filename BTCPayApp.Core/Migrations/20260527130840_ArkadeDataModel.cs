using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class ArkadeDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelAliases");

            migrationBuilder.DropTable(
                name: "LightningPayments");

            migrationBuilder.DropTable(
                name: "OutboxItems");

            migrationBuilder.DropTable(
                name: "LightningChannels");

            migrationBuilder.DropIndex(
                name: "IX_Settings_EntityKey",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Backup",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "EntityKey",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Settings");

            migrationBuilder.EnsureSchema(
                name: "ark");

            migrationBuilder.RenameTable(
                name: "Settings",
                newName: "Settings",
                newSchema: "ark");

            migrationBuilder.CreateTable(
                name: "Intents",
                schema: "ark",
                columns: table => new
                {
                    IntentTxId = table.Column<string>(type: "TEXT", nullable: false),
                    IntentId = table.Column<string>(type: "TEXT", nullable: true),
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidFrom = table.Column<long>(type: "INTEGER", nullable: true),
                    ValidUntil = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RegisterProof = table.Column<string>(type: "TEXT", nullable: false),
                    RegisterProofMessage = table.Column<string>(type: "TEXT", nullable: false),
                    DeleteProof = table.Column<string>(type: "TEXT", nullable: false),
                    DeleteProofMessage = table.Column<string>(type: "TEXT", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", nullable: true),
                    CommitmentTransactionId = table.Column<string>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", nullable: true),
                    PartialForfeits = table.Column<string>(type: "TEXT", nullable: false),
                    SignerDescriptor = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Intents", x => x.IntentTxId);
                });

            migrationBuilder.CreateTable(
                name: "Vtxos",
                schema: "ark",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionOutputIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Script = table.Column<string>(type: "TEXT", nullable: false),
                    SpentByTransactionId = table.Column<string>(type: "TEXT", nullable: true),
                    SettledByTransactionId = table.Column<string>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    SeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Recoverable = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtHeight = table.Column<uint>(type: "INTEGER", nullable: true),
                    Preconfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Unrolled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CommitmentTxids = table.Column<string>(type: "TEXT", nullable: true),
                    ArkTxid = table.Column<string>(type: "TEXT", nullable: true),
                    AssetsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vtxos", x => new { x.TransactionId, x.TransactionOutputIndex });
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                schema: "ark",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Wallet = table.Column<string>(type: "TEXT", nullable: false),
                    WalletDestination = table.Column<string>(type: "TEXT", nullable: true),
                    WalletType = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    AccountDescriptor = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "TODO_MIGRATION"),
                    LastUsedIndex = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntentVtxos",
                schema: "ark",
                columns: table => new
                {
                    IntentTxId = table.Column<string>(type: "TEXT", nullable: false),
                    VtxoTransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    VtxoTransactionOutputIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentVtxos", x => new { x.IntentTxId, x.VtxoTransactionId, x.VtxoTransactionOutputIndex });
                    table.ForeignKey(
                        name: "FK_IntentVtxos_Intents_IntentTxId",
                        column: x => x.IntentTxId,
                        principalSchema: "ark",
                        principalTable: "Intents",
                        principalColumn: "IntentTxId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntentVtxos_Vtxos_VtxoTransactionId_VtxoTransactionOutputIndex",
                        columns: x => new { x.VtxoTransactionId, x.VtxoTransactionOutputIndex },
                        principalSchema: "ark",
                        principalTable: "Vtxos",
                        principalColumns: new[] { "TransactionId", "TransactionOutputIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletContracts",
                schema: "ark",
                columns: table => new
                {
                    Script = table.Column<string>(type: "TEXT", nullable: false),
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityState = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ContractData = table.Column<string>(type: "jsonb", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletContracts", x => new { x.Script, x.WalletId });
                    table.ForeignKey(
                        name: "FK_WalletContracts_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "ark",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Swaps",
                schema: "ark",
                columns: table => new
                {
                    SwapId = table.Column<string>(type: "TEXT", nullable: false),
                    WalletId = table.Column<string>(type: "TEXT", nullable: false),
                    SwapType = table.Column<int>(type: "INTEGER", nullable: false),
                    Invoice = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    ContractScript = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailReason = table.Column<string>(type: "TEXT", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Swaps", x => new { x.SwapId, x.WalletId });
                    table.ForeignKey(
                        name: "FK_Swaps_WalletContracts_ContractScript_WalletId",
                        columns: x => new { x.ContractScript, x.WalletId },
                        principalSchema: "ark",
                        principalTable: "WalletContracts",
                        principalColumns: new[] { "Script", "WalletId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Swaps_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "ark",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Intents_IntentId",
                schema: "ark",
                table: "Intents",
                column: "IntentId",
                unique: true,
                filter: "\"IntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntentVtxos_VtxoTransactionId_VtxoTransactionOutputIndex",
                schema: "ark",
                table: "IntentVtxos",
                columns: new[] { "VtxoTransactionId", "VtxoTransactionOutputIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_ContractScript_WalletId",
                schema: "ark",
                table: "Swaps",
                columns: new[] { "ContractScript", "WalletId" });

            migrationBuilder.CreateIndex(
                name: "IX_Swaps_WalletId",
                schema: "ark",
                table: "Swaps",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletContracts_WalletId",
                schema: "ark",
                table: "WalletContracts",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Wallet",
                schema: "ark",
                table: "Wallets",
                column: "Wallet",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntentVtxos",
                schema: "ark");

            migrationBuilder.DropTable(
                name: "Swaps",
                schema: "ark");

            migrationBuilder.DropTable(
                name: "Intents",
                schema: "ark");

            migrationBuilder.DropTable(
                name: "Vtxos",
                schema: "ark");

            migrationBuilder.DropTable(
                name: "WalletContracts",
                schema: "ark");

            migrationBuilder.DropTable(
                name: "Wallets",
                schema: "ark");

            migrationBuilder.RenameTable(
                name: "Settings",
                schema: "ark",
                newName: "Settings");

            migrationBuilder.AddColumn<bool>(
                name: "Backup",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EntityKey",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "LightningChannels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AdditionalData = table.Column<string>(type: "jsonb", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Checkpoint = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: true),
                    EntityKey = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightningChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LightningPayments",
                columns: table => new
                {
                    PaymentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Inbound = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaymentId = table.Column<string>(type: "TEXT", nullable: false),
                    AdditionalData = table.Column<string>(type: "jsonb", nullable: false),
                    EntityKey = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentRequest = table.Column<string>(type: "TEXT", nullable: true),
                    Preimage = table.Column<string>(type: "TEXT", nullable: true),
                    Secret = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightningPayments", x => new { x.PaymentHash, x.Inbound, x.PaymentId });
                });

            migrationBuilder.CreateTable(
                name: "OutboxItems",
                columns: table => new
                {
                    Entity = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxItems", x => new { x.Entity, x.Key, x.ActionType, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "ChannelAliases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAliases_LightningChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "LightningChannels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_EntityKey",
                table: "Settings",
                column: "EntityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAliases_ChannelId",
                table: "ChannelAliases",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_LightningChannels_EntityKey",
                table: "LightningChannels",
                column: "EntityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LightningPayments_EntityKey",
                table: "LightningPayments",
                column: "EntityKey",
                unique: true);
        }
    }
}
