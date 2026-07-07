namespace BTCPayApp.Core.BTCPayServer;

/// <summary>
/// Snapshot of the paired BTCPay store's Arkade-plugin network configuration —
/// the on-device app inherits these values via <see cref="IBTCPayAppHubServer.GetArkadeConfig"/>
/// instead of letting the merchant pick a network on the device. Mirrors
/// <see cref="NArk.Hosting.ArkNetworkConfig"/> on the wire so the device can
/// reconstruct the SDK's network config verbatim.
/// </summary>
/// <param name="ArkUri">Arkade operator gateway. Maps to <c>ArkNetworkConfig.ArkUri</c>.</param>
/// <param name="ArkadeWalletUri">Optional Arkade wallet front-end URL.</param>
/// <param name="BoltzUri">Optional Boltz swap-service endpoint for this network.</param>
/// <param name="ExplorerUri">Optional block-explorer URL for this network.</param>
/// <param name="EsploraUri">Optional Esplora REST endpoint backing on-chain queries.</param>
/// <param name="ElectrumWsUri">Optional Electrum WebSocket endpoint.</param>
/// <param name="ElectrumTcpUri">Optional Electrum TCP endpoint.</param>
/// <param name="NetworkType">
/// BTCPay's <c>NetworkType</c> label — <c>Mainnet</c>, <c>Testnet</c>,
/// <c>Regtest</c> or <c>Signet</c>. Derived from the server's
/// <c>BTCPayNetworkProvider.BTC.NBitcoinNetwork.ChainName</c>.
/// </param>
public record ArkadeServerConfigDto(
    string ArkUri,
    string? ArkadeWalletUri,
    string? BoltzUri,
    string? ExplorerUri,
    string? EsploraUri,
    string? ElectrumWsUri,
    string? ElectrumTcpUri,
    string NetworkType);
