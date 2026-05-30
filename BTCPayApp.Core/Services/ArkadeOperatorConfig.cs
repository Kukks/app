using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using NArk.Hosting;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Resolves the on-device <see cref="ArkNetworkConfig"/> from the snapshot of
/// the paired BTCPay store's btcpay-arkade plugin configuration. The device
/// no longer picks a network — <see cref="ArkadeConfigSyncService"/> fetches
/// an <see cref="ArkadeServerConfigDto"/> from the hub and persists it via
/// <see cref="SetServerConfigAsync"/>; <see cref="ResolveAsync"/> then rebuilds
/// <see cref="ArkNetworkConfig"/> verbatim from that snapshot.
/// </summary>
/// <remarks>
/// On a pre-pair first launch (nothing synced yet) <see cref="ResolveAsync"/>
/// falls back to the bundled default via <see cref="ArkConfiguration.Resolve"/>
/// so the host can still start. Once the device pairs and the sync service
/// runs, the merchant's server-side Arkade config wins.
/// </remarks>
public class ArkadeOperatorConfig(ConfigProvider configProvider)
{
    public const string NetworkKey = "ark:network";
    public const string ServerConfigKey = "ark:operator:server-config";

    public async Task<ArkNetworkConfig> ResolveAsync()
    {
        var serverConfig = await configProvider.Get<ArkadeServerConfigDto>(ServerConfigKey);
        if (serverConfig is not null)
        {
            return new ArkNetworkConfig(
                ArkUri: serverConfig.ArkUri,
                ArkadeWalletUri: serverConfig.ArkadeWalletUri,
                BoltzUri: serverConfig.BoltzUri,
                ExplorerUri: serverConfig.ExplorerUri,
                EsploraUri: serverConfig.EsploraUri,
                ElectrumWsUri: serverConfig.ElectrumWsUri,
                ElectrumTcpUri: serverConfig.ElectrumTcpUri);
        }

        // Pre-pair fallback: nothing synced yet, use the bundled default so the
        // host can still start. The first hub-connected event will overwrite
        // this with the paired store's actual Arkade config.
        var networkName = await configProvider.Get<string>(NetworkKey);
        return ArkConfiguration.Resolve(networkName);
    }

    public async Task<string> GetNetworkNameAsync()
        => (await configProvider.Get<string>(NetworkKey)) ?? ArkConfiguration.DefaultNetwork;

    public async Task<string?> GetEndpointOverrideAsync()
        => (await configProvider.Get<ArkadeServerConfigDto>(ServerConfigKey))?.ArkUri;

    /// <summary>
    /// Returns the persisted snapshot of the paired BTCPay store's Arkade
    /// network config, or <c>null</c> if nothing has been synced yet.
    /// </summary>
    public async Task<ArkadeServerConfigDto?> GetServerConfigAsync()
        => await configProvider.Get<ArkadeServerConfigDto>(ServerConfigKey);

    /// <summary>
    /// Persists the paired BTCPay store's Arkade network snapshot. Called only
    /// by <see cref="ArkadeConfigSyncService"/>. Also mirrors
    /// <see cref="ArkadeServerConfigDto.NetworkType"/> under
    /// <see cref="NetworkKey"/> so existing readers
    /// (<see cref="GetNetworkNameAsync"/>, the mainnet preflight gate) keep
    /// working.
    /// </summary>
    public async Task SetServerConfigAsync(ArkadeServerConfigDto dto)
    {
        await configProvider.Set(ServerConfigKey, dto, backup: false);
        await configProvider.Set(NetworkKey, dto.NetworkType, backup: false);
    }
}
