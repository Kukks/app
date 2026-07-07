using BTCPayApp.Core.Models;
using BTCPayApp.Core.Services;
using NBitcoin;
using Xunit;

namespace BTCPayApp.Tests;

public class SignerStatusServiceTests
{
    private static (SignerStatusService svc, InMemoryConfigProvider cfg, MnemonicBackupService backup, ArkadeOperatorConfig op) Build()
    {
        var cfg = new InMemoryConfigProvider();
        var backup = new MnemonicBackupService(cfg);
        var op = new ArkadeOperatorConfig(cfg);
        var svc = new SignerStatusService(cfg, backup, op);
        return (svc, cfg, backup, op);
    }

    [Fact]
    public async Task Unsafe_when_no_mnemonic()
    {
        var (svc, _, _, _) = Build();
        svc.UpdateHubConnected(true);

        var status = await svc.SnapshotAsync();

        Assert.Equal(SignerHealthLevel.Unsafe, status.Level);
    }

    [Fact]
    public async Task Degraded_when_hub_disconnected()
    {
        var (svc, cfg, backup, _) = Build();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);
        await backup.MarkBackupVerifiedAsync();
        svc.UpdateHubConnected(false);

        var status = await svc.SnapshotAsync();

        Assert.Equal(SignerHealthLevel.Degraded, status.Level);
    }

    [Fact]
    public async Task Degraded_when_backup_pending()
    {
        var (svc, cfg, _, _) = Build();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);
        svc.UpdateHubConnected(true);

        var status = await svc.SnapshotAsync();

        Assert.Equal(SignerHealthLevel.Degraded, status.Level);
    }

    [Fact]
    public async Task Healthy_when_all_green()
    {
        var (svc, cfg, backup, _) = Build();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);
        await backup.MarkBackupVerifiedAsync();
        svc.UpdateHubConnected(true);

        var status = await svc.SnapshotAsync();

        Assert.Equal(SignerHealthLevel.Healthy, status.Level);
    }

    [Fact]
    public async Task LastSignAt_bumps_on_RecordSign()
    {
        var (svc, cfg, _, _) = Build();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        await cfg.Set(ArkWalletBootstrapService.WalletIdKey, "wid-1", backup: false);

        var before = (await svc.SnapshotAsync()).LastSignAt;
        svc.RecordSign();
        var after = (await svc.SnapshotAsync()).LastSignAt;

        Assert.Null(before);
        Assert.NotNull(after);
    }
}
