using NArk.Hosting;

namespace BTCPayApp.Core;

/// <summary>
/// Selects which Arkade network the app connects to. Mutinynet (signet) is the
/// default; the value is resolvable by name so regtest/mainnet can be selected
/// later (e.g. from app config) without touching the DI wiring.
/// </summary>
public static class ArkConfiguration
{
    /// <summary>
    /// The default network name the app uses when nothing else is configured.
    /// </summary>
    public const string DefaultNetwork = "mutinynet";

    /// <summary>
    /// Resolves an <see cref="ArkNetworkConfig"/> from a network name. Accepts
    /// <c>mainnet</c>, <c>mutinynet</c> (signet) and <c>regtest</c>
    /// (case-insensitive). A null/empty/unknown name falls back to the
    /// <see cref="DefaultNetwork"/> (Mutinynet).
    /// </summary>
    public static ArkNetworkConfig Resolve(string? network = null)
    {
        return (network ?? DefaultNetwork).Trim().ToLowerInvariant() switch
        {
            "mainnet" => ArkNetworkConfig.Mainnet,
            "regtest" => ArkNetworkConfig.Regtest,
            "mutinynet" or "signet" => ArkNetworkConfig.Mutinynet,
            _ => ArkNetworkConfig.Mutinynet
        };
    }
}
