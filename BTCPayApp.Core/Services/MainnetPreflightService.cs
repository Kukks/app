using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Checks that the device is in a safe state to accept mainnet payments —
/// mnemonic backed up + verified, owner wallet registered with the chosen
/// operator, hub connected. On any non-mainnet network the result is
/// "not applicable" so testnet/regtest demos aren't gated.
/// </summary>
public class MainnetPreflightService(
    ConfigProvider configProvider,
    MnemonicBackupService backupService,
    ArkadeOperatorConfig operatorConfig,
    SignerStatusService statusService)
{
    public async Task<PreflightResult> EvaluateAsync()
    {
        var network = await operatorConfig.GetNetworkNameAsync();
        if (!string.Equals(network, "mainnet", StringComparison.OrdinalIgnoreCase))
            return PreflightResult.Skipped();

        var snapshot = await statusService.SnapshotAsync();
        var checks = new List<PreflightCheck>
        {
            new("Mnemonic backed up and verified", await backupService.IsBackupVerifiedAsync()),
            new("Device paired and connected", snapshot.HubConnected),
            new("Owner wallet registered with operator", !string.IsNullOrEmpty(snapshot.OwnerWalletId)),
        };

        return new PreflightResult(checks.All(c => c.Passed), NotApplicable: false, checks);
    }
}
