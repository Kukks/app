using BTCPayApp.Core.Services;
using NBitcoin;
using Xunit;

namespace BTCPayApp.Tests;

public class MainnetPreflightServiceTests
{
    private static MainnetPreflightService Build(InMemoryConfigProvider cfg, SignerStatusService status)
        => new(cfg, new MnemonicBackupService(cfg), new ArkadeOperatorConfig(cfg), status);

    [Fact]
    public async Task NotApplicable_when_not_mainnet()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mutinynet", backup: false);
        var status = new SignerStatusService(cfg, new MnemonicBackupService(cfg), new ArkadeOperatorConfig(cfg));
        var sut = Build(cfg, status);

        var result = await sut.EvaluateAsync();

        Assert.True(result.NotApplicable);
    }

    [Fact]
    public async Task Mainnet_fails_when_backup_pending()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mainnet", backup: false);
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);
        var status = new SignerStatusService(cfg, new MnemonicBackupService(cfg), new ArkadeOperatorConfig(cfg));
        status.UpdateHubConnected(true);
        var sut = Build(cfg, status);

        var result = await sut.EvaluateAsync();

        Assert.False(result.NotApplicable);
        Assert.False(result.AllPassed);
        Assert.Contains(result.Checks, c => c.Label.Contains("backed up") && !c.Passed);
    }

    [Fact]
    public async Task Mainnet_passes_when_all_green()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkadeOperatorConfig.NetworkKey, "mainnet", backup: false);
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);
        var backup = new MnemonicBackupService(cfg);
        await backup.MarkBackupVerifiedAsync();
        var status = new SignerStatusService(cfg, backup, new ArkadeOperatorConfig(cfg));
        status.UpdateHubConnected(true);
        var sut = Build(cfg, status);

        var result = await sut.EvaluateAsync();

        Assert.True(result.AllPassed);
    }
}
