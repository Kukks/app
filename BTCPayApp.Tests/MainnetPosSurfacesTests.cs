using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;
using BTCPayApp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

/// <summary>
/// End-to-end exercise of the new mainnet-ready POS surfaces (Plan 2 Task 6)
/// against a real <see cref="HeadlessTestNode"/>. Does not depend on an Arkade
/// operator being reachable: <see cref="ArkWalletBootstrapService"/> generates
/// and persists the on-device mnemonic synchronously at start, while the
/// remote wallet registration runs on a background retry loop — so the
/// states we want to verify here ("mnemonic present, walletId pending") are
/// the natural post-start state without external dependencies.
/// </summary>
public class MainnetPosSurfacesTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Mnemonic_persists_on_first_start_and_backup_flag_round_trips()
    {
        using var node = await HeadlessTestNode.Create(nameof(Mnemonic_persists_on_first_start_and_backup_flag_round_trips), output);

        var backup = node.App.Services.GetRequiredService<MnemonicBackupService>();
        var configProvider = node.App.Services.GetRequiredService<ConfigProvider>();

        // ArkWalletBootstrapService runs on host start and writes the mnemonic
        // into ConfigProvider before any background work — the moment the host
        // is up, the seed exists.
        string? mnemonic = null;
        await TestUtils.EventuallyAsync(async () =>
        {
            mnemonic = await backup.GetMnemonicAsync();
            Assert.False(string.IsNullOrEmpty(mnemonic), "Mnemonic should be persisted by ArkWalletBootstrapService at start.");
        });
        Assert.NotNull(mnemonic);

        // 12 or 24 words — BIP-39 only allows these in the English wordlist used by NBitcoin.Mnemonic.
        var wordCount = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(wordCount is 12 or 24, $"Expected BIP-39 word count (12 or 24), got {wordCount}");

        // Initially not verified.
        Assert.False(await backup.IsBackupVerifiedAsync());
        Assert.Null(await backup.GetBackupVerifiedAtAsync());

        // After marking, the flag persists and the timestamp is recent.
        await backup.MarkBackupVerifiedAsync();
        Assert.True(await backup.IsBackupVerifiedAsync());
        var verifiedAt = await backup.GetBackupVerifiedAtAsync();
        Assert.NotNull(verifiedAt);
        Assert.InRange(DateTimeOffset.UtcNow - verifiedAt.Value, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        // Verification-position picker honours the actual mnemonic length.
        var positions = backup.ChooseVerificationPositions(mnemonic, 4);
        Assert.Equal(4, positions.Count);
        Assert.Equal(4, positions.Distinct().Count());
        Assert.All(positions, p => Assert.InRange(p, 1, wordCount));
    }

    [Fact]
    public async Task OperatorConfig_persists_network_choice_and_returns_correct_default()
    {
        using var node = await HeadlessTestNode.Create(nameof(OperatorConfig_persists_network_choice_and_returns_correct_default), output);

        var op = node.App.Services.GetRequiredService<ArkadeOperatorConfig>();

        // First-launch default: mutinynet (safe for accidental opens).
        Assert.Equal("mutinynet", await op.GetNetworkNameAsync());
        Assert.Null(await op.GetEndpointOverrideAsync());

        // Persist a mainnet choice with a custom operator endpoint.
        await op.SetAsync("mainnet", "https://operator.example/");
        Assert.Equal("mainnet", await op.GetNetworkNameAsync());
        Assert.Equal("https://operator.example/", await op.GetEndpointOverrideAsync());

        // ResolveAsync applies the override on top of mainnet defaults.
        var resolved = await op.ResolveAsync();
        Assert.Equal("https://operator.example/", resolved.ArkUri);

        // Switching back to mutinynet with no override resolves to the bundled mutinynet defaults.
        await op.SetAsync("mutinynet", null);
        var resolvedMutiny = await op.ResolveAsync();
        Assert.Equal(NArk.Hosting.ArkNetworkConfig.Mutinynet.ArkUri, resolvedMutiny.ArkUri);
    }

    [Fact]
    public async Task SignerStatus_reflects_bootstrap_progress_and_backup_state()
    {
        using var node = await HeadlessTestNode.Create(nameof(SignerStatus_reflects_bootstrap_progress_and_backup_state), output);

        var status = node.App.Services.GetRequiredService<SignerStatusService>();
        var backup = node.App.Services.GetRequiredService<MnemonicBackupService>();

        // First snapshot after mnemonic exists but before wallet registration succeeds:
        // walletId is null until the operator answers, so health is Unsafe.
        await TestUtils.EventuallyAsync(async () =>
        {
            var snap = await status.SnapshotAsync();
            Assert.False(string.IsNullOrEmpty(await backup.GetMnemonicAsync()));
            // Either the operator is unreachable in this sandbox (walletId stays null → Unsafe),
            // or it has reached the operator and walletId is set → Degraded (backup still pending).
            Assert.True(snap.Level is SignerHealthLevel.Unsafe or SignerHealthLevel.Degraded);
        });

        // Mark backup verified — Healthy still requires hub-connected AND walletId, so this
        // by itself does NOT make it Healthy in the sandbox (no BTCPay = no hub). What it does
        // is flip the backup-verified leg, which preflight + signer-status both observe.
        await backup.MarkBackupVerifiedAsync();
        var afterBackup = await status.SnapshotAsync();
        Assert.True(afterBackup.BackupVerified);
    }

    [Fact]
    public async Task ArkSignerService_KnowsWallet_recognises_owner_only()
    {
        using var node = await HeadlessTestNode.Create(nameof(ArkSignerService_KnowsWallet_recognises_owner_only), output);

        var signer = node.App.Services.GetRequiredService<ArkSignerService>();
        var configProvider = node.App.Services.GetRequiredService<ConfigProvider>();

        // No walletId persisted yet → unknown for any input (the bootstrap retry loop
        // only sets it after a successful operator round-trip, which may not happen in
        // this sandbox). Empty walletId always false by contract.
        Assert.False(await signer.KnowsWalletAsync(""));
        Assert.False(await signer.KnowsWalletAsync("not-the-owner"));

        // Simulate a successful wallet registration by writing the key directly.
        await configProvider.Set(ArkWalletBootstrapService.WalletIdKey, "owner-wallet-id", backup: false);
        Assert.True(await signer.KnowsWalletAsync("owner-wallet-id"));
        Assert.False(await signer.KnowsWalletAsync("some-other-wallet"));
    }

    [Fact]
    public async Task MainnetPreflight_is_skipped_off_mainnet_and_enforced_on_mainnet()
    {
        using var node = await HeadlessTestNode.Create(nameof(MainnetPreflight_is_skipped_off_mainnet_and_enforced_on_mainnet), output);

        var preflight = node.App.Services.GetRequiredService<MainnetPreflightService>();
        var op = node.App.Services.GetRequiredService<ArkadeOperatorConfig>();
        var backup = node.App.Services.GetRequiredService<MnemonicBackupService>();
        var configProvider = node.App.Services.GetRequiredService<ConfigProvider>();

        // Default mutinynet → preflight is "not applicable".
        var skipped = await preflight.EvaluateAsync();
        Assert.True(skipped.NotApplicable);
        Assert.True(skipped.AllPassed); // NotApplicable is treated as a pass for the POS gate.

        // Switching to mainnet enables the gate.
        await op.SetAsync("mainnet", null);

        // Backup not verified AND walletId not yet present → all checks fail.
        var blocked = await preflight.EvaluateAsync();
        Assert.False(blocked.NotApplicable);
        Assert.False(blocked.AllPassed);
        Assert.Contains(blocked.Checks, c => c.Label.Contains("backed up", StringComparison.OrdinalIgnoreCase) && !c.Passed);

        // Satisfy all three gates and re-evaluate.
        await backup.MarkBackupVerifiedAsync();
        await configProvider.Set(ArkWalletBootstrapService.WalletIdKey, "owner-wallet-id", backup: false);
        node.App.Services.GetRequiredService<SignerStatusService>().UpdateHubConnected(true);

        var passed = await preflight.EvaluateAsync();
        Assert.False(passed.NotApplicable);
        Assert.True(passed.AllPassed,
            $"Expected all preflight checks to pass; failing: {string.Join(", ", passed.Checks.Where(c => !c.Passed).Select(c => c.Label))}");
    }
}
