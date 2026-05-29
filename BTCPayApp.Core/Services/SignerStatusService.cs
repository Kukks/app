using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Models;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Single source of truth for "is the merchant ready to accept payment". Composes
/// the on-device wallet state (mnemonic + walletId + backup-verified), hub
/// connectivity (set by <c>BTCPayConnectionManager</c>), and the merchant's
/// selected network into a <see cref="SignerStatus"/>. UI components render a
/// red/yellow/green badge from <see cref="SignerHealthLevel"/>.
/// </summary>
public class SignerStatusService(
    ConfigProvider configProvider,
    MnemonicBackupService backupService,
    ArkadeOperatorConfig operatorConfig)
{
    private bool _hubConnected;
    private DateTimeOffset? _lastSignAt;
    private string? _lastError;

    public void UpdateHubConnected(bool connected) => _hubConnected = connected;
    public void RecordSign() => _lastSignAt = DateTimeOffset.UtcNow;
    public void RecordError(string message) => _lastError = message;

    public async Task<SignerStatus> SnapshotAsync()
    {
        var mnemonic = await configProvider.Get<string>(ArkWalletBootstrapService.MnemonicKey);
        var walletId = await configProvider.Get<string>(ArkWalletBootstrapService.WalletIdKey);
        var backupVerified = await backupService.IsBackupVerifiedAsync();
        var network = await operatorConfig.GetNetworkNameAsync();

        var level =
            string.IsNullOrEmpty(mnemonic) || string.IsNullOrEmpty(walletId) ? SignerHealthLevel.Unsafe :
            (!_hubConnected || !backupVerified)                              ? SignerHealthLevel.Degraded :
                                                                               SignerHealthLevel.Healthy;

        return new SignerStatus(level, walletId, _lastError, _lastSignAt, _hubConnected, backupVerified, network);
    }
}
