using BTCPayApp.Core.Contracts;
using NArk.Hosting;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Reads the merchant-selected Arkade network and operator endpoint override from
/// <see cref="ConfigProvider"/> and produces the <see cref="ArkNetworkConfig"/>
/// the SDK transport should use. An empty/whitespace override falls back to the
/// bundled network default — so the merchant can pick "Mainnet" without having
/// to know an operator URL.
/// </summary>
/// <remarks>
/// The "operator endpoint" maps to <see cref="ArkNetworkConfig.ArkUri"/> — that's
/// the Arkade operator gateway the merchant is choosing. Other URIs (Explorer,
/// Esplora, Boltz, …) stay at the bundled per-network defaults: a merchant
/// pointing at a custom operator still uses the network's standard mempool
/// and Boltz endpoints unless we extend this with per-service overrides later.
/// </remarks>
public class ArkadeOperatorConfig(ConfigProvider configProvider)
{
    public const string NetworkKey = "ark:network";
    public const string OperatorEndpointKey = "ark:operator:endpoint";

    public async Task<ArkNetworkConfig> ResolveAsync()
    {
        var networkName = await configProvider.Get<string>(NetworkKey);
        var resolved = ArkConfiguration.Resolve(networkName);

        var endpointOverride = await configProvider.Get<string>(OperatorEndpointKey);
        if (!string.IsNullOrWhiteSpace(endpointOverride))
            resolved = resolved with { ArkUri = endpointOverride.Trim() };

        return resolved;
    }

    public async Task<string> GetNetworkNameAsync()
        => (await configProvider.Get<string>(NetworkKey)) ?? ArkConfiguration.DefaultNetwork;

    public async Task<string?> GetEndpointOverrideAsync()
        => await configProvider.Get<string>(OperatorEndpointKey);

    public async Task SetAsync(string networkName, string? endpointOverride)
    {
        await configProvider.Set(NetworkKey, networkName, backup: false);
        await configProvider.Set(OperatorEndpointKey,
            string.IsNullOrWhiteSpace(endpointOverride) ? null : endpointOverride.Trim(),
            backup: false);
    }
}
