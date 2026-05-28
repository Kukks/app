using BTCPayApp.Core.Services;
using NBitcoin;
using Xunit;

namespace BTCPayApp.Tests;

public class MnemonicBackupServiceTests
{
    [Fact]
    public async Task IsBackupVerified_returns_false_when_key_missing()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        var svc = new MnemonicBackupService(cfg);

        Assert.False(await svc.IsBackupVerifiedAsync());
    }

    [Fact]
    public async Task MarkBackupVerified_flips_state()
    {
        var cfg = new InMemoryConfigProvider();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, new Mnemonic(Wordlist.English).ToString(), backup: false);
        var svc = new MnemonicBackupService(cfg);

        await svc.MarkBackupVerifiedAsync();

        Assert.True(await svc.IsBackupVerifiedAsync());
    }

    [Fact]
    public async Task GetMnemonic_roundtrips()
    {
        var cfg = new InMemoryConfigProvider();
        var m = new Mnemonic(Wordlist.English).ToString();
        await cfg.Set(ArkWalletBootstrapService.MnemonicKey, m, backup: false);
        var svc = new MnemonicBackupService(cfg);

        Assert.Equal(m, await svc.GetMnemonicAsync());
    }

    [Theory]
    [InlineData(12, 4)]
    [InlineData(24, 4)]
    [InlineData(24, 6)]
    public void ChooseVerificationPositions_count_unique_in_range(int wordCount, int count)
    {
        var cfg = new InMemoryConfigProvider();
        var svc = new MnemonicBackupService(cfg);
        var words = string.Join(' ', Enumerable.Range(0, wordCount).Select(_ => "x"));

        var positions = svc.ChooseVerificationPositions(words, count);

        Assert.Equal(count, positions.Count);
        Assert.Equal(count, positions.Distinct().Count());
        Assert.All(positions, p => Assert.InRange(p, 1, wordCount));
    }
}
