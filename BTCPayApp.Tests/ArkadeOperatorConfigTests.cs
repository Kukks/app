using BTCPayApp.Core;
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
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mainnet", backup: false);
        var sut = new ArkadeOperatorConfig(cfg);

        var resolved = await sut.ResolveAsync();

        Assert.Equal(ArkNetworkConfig.Mainnet.ArkUri, resolved.ArkUri);
    }

    [Fact]
    public async Task Resolve_applies_operator_override()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mutinynet", backup: false);
        await cfg.Set(ArkadeOperatorConfig.OperatorEndpointKey, "https://operator.example/", backup: false);
        var sut = new ArkadeOperatorConfig(cfg);

        var resolved = await sut.ResolveAsync();

        Assert.Equal("https://operator.example/", resolved.ArkUri);
        // Other URIs stay at the chosen network's defaults.
        Assert.Equal(ArkNetworkConfig.Mutinynet.EsploraUri, resolved.EsploraUri);
    }

    [Fact]
    public async Task Resolve_ignores_blank_operator_override()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mutinynet", backup: false);
        await cfg.Set(ArkadeOperatorConfig.OperatorEndpointKey, "   ", backup: false);
        var sut = new ArkadeOperatorConfig(cfg);

        var resolved = await sut.ResolveAsync();

        Assert.Equal(ArkNetworkConfig.Mutinynet.ArkUri, resolved.ArkUri);
    }
}
