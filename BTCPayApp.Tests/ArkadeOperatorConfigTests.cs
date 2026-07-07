using BTCPayApp.Core;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Services;
using NArk.Hosting;
using Xunit;

namespace BTCPayApp.Tests;

public class ArkadeOperatorConfigTests
{
    [Fact]
    public async Task Resolve_uses_default_network_when_unset()
    {
        var cfg = new InMemoryConfigProvider();
        var sut = new ArkadeOperatorConfig(cfg);

        var resolved = await sut.ResolveAsync();

        Assert.Equal(ArkNetworkConfig.Mutinynet.ArkUri, resolved.ArkUri);
    }

    [Fact]
    public async Task Resolve_picks_mainnet_when_persisted()
    {
        var cfg = new InMemoryConfigProvider();
        var sut = new ArkadeOperatorConfig(cfg);
        await sut.SetServerConfigAsync(new ArkadeServerConfigDto(
            ArkUri: ArkNetworkConfig.Mainnet.ArkUri,
            ArkadeWalletUri: ArkNetworkConfig.Mainnet.ArkadeWalletUri,
            BoltzUri: ArkNetworkConfig.Mainnet.BoltzUri,
            ExplorerUri: ArkNetworkConfig.Mainnet.ExplorerUri,
            EsploraUri: ArkNetworkConfig.Mainnet.EsploraUri,
            ElectrumWsUri: ArkNetworkConfig.Mainnet.ElectrumWsUri,
            ElectrumTcpUri: ArkNetworkConfig.Mainnet.ElectrumTcpUri,
            NetworkType: "Mainnet"));

        var resolved = await sut.ResolveAsync();

        Assert.Equal(ArkNetworkConfig.Mainnet.ArkUri, resolved.ArkUri);
    }

    [Fact]
    public async Task Resolve_applies_operator_override()
    {
        var cfg = new InMemoryConfigProvider();
        var sut = new ArkadeOperatorConfig(cfg);
        await sut.SetServerConfigAsync(new ArkadeServerConfigDto(
            ArkUri: "https://operator.example/",
            ArkadeWalletUri: ArkNetworkConfig.Mutinynet.ArkadeWalletUri,
            BoltzUri: ArkNetworkConfig.Mutinynet.BoltzUri,
            ExplorerUri: ArkNetworkConfig.Mutinynet.ExplorerUri,
            EsploraUri: ArkNetworkConfig.Mutinynet.EsploraUri,
            ElectrumWsUri: ArkNetworkConfig.Mutinynet.ElectrumWsUri,
            ElectrumTcpUri: ArkNetworkConfig.Mutinynet.ElectrumTcpUri,
            NetworkType: "Signet"));

        var resolved = await sut.ResolveAsync();

        Assert.Equal("https://operator.example/", resolved.ArkUri);
        // Other URIs come straight from the server-side snapshot.
        Assert.Equal(ArkNetworkConfig.Mutinynet.EsploraUri, resolved.EsploraUri);
    }

    [Fact]
    public async Task Resolve_falls_back_to_default_when_no_server_config()
    {
        var cfg = new InMemoryConfigProvider();
        var sut = new ArkadeOperatorConfig(cfg);

        // No SetServerConfigAsync call — device hasn't paired yet. ResolveAsync
        // must still produce a usable config (the bundled default) so the host
        // can start; the first hub-connected event will overwrite it.
        var resolved = await sut.ResolveAsync();

        Assert.Equal(ArkNetworkConfig.Mutinynet.ArkUri, resolved.ArkUri);
        Assert.Null(await sut.GetServerConfigAsync());
    }

    [Fact]
    public async Task Server_config_snapshot_wins_over_default_resolve()
    {
        var cfg = new InMemoryConfigProvider();
        var sut = new ArkadeOperatorConfig(cfg);

        // Server snapshot: an arbitrary regtest-style operator on a host
        // ArkConfiguration.Resolve would never pick on its own.
        var dto = new ArkadeServerConfigDto(
            ArkUri: "http://custom-operator.local:7070",
            ArkadeWalletUri: "http://custom-wallet.local:3002",
            BoltzUri: "http://custom-boltz.local:9069/",
            ExplorerUri: "http://custom-explorer.local:7080",
            EsploraUri: "http://custom-esplora.local:3000",
            ElectrumWsUri: "ws://custom-electrum.local:50003",
            ElectrumTcpUri: "tcp://custom-electrum.local:50000",
            NetworkType: "Regtest");
        await sut.SetServerConfigAsync(dto);

        var resolved = await sut.ResolveAsync();

        Assert.Equal(dto.ArkUri, resolved.ArkUri);
        Assert.Equal(dto.EsploraUri, resolved.EsploraUri);
        Assert.Equal(dto.ExplorerUri, resolved.ExplorerUri);
        Assert.Equal(dto.ElectrumWsUri, resolved.ElectrumWsUri);
        Assert.Equal(dto.ElectrumTcpUri, resolved.ElectrumTcpUri);
        Assert.Equal("Regtest", await sut.GetNetworkNameAsync());
        Assert.Equal("http://custom-operator.local:7070", await sut.GetEndpointOverrideAsync());
    }
}
