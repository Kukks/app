using System.Security.Cryptography;
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Tracks whether the on-device owner mnemonic has been backed up and verified
/// by the merchant, and helps the verify-step page pick random word positions
/// to challenge the merchant against.
/// </summary>
/// <remarks>
/// Reads the mnemonic from the same <see cref="ConfigProvider"/> key
/// (<see cref="ArkWalletBootstrapService.MnemonicKey"/>) that
/// <see cref="ArkWalletBootstrapService"/> writes on first run. The verified
/// flag is stored at <see cref="BackupVerifiedKey"/> as an ISO-8601 timestamp;
/// any non-empty value means "verified".
/// </remarks>
public class MnemonicBackupService(ConfigProvider configProvider)
{
    public const string BackupVerifiedKey = "ark:owner:backup-verified-at";

    public Task<string?> GetMnemonicAsync()
        => configProvider.Get<string>(ArkWalletBootstrapService.MnemonicKey);

    public async Task<bool> IsBackupVerifiedAsync()
        => !string.IsNullOrEmpty(await configProvider.Get<string>(BackupVerifiedKey));

    public async Task<DateTimeOffset?> GetBackupVerifiedAtAsync()
    {
        var raw = await configProvider.Get<string>(BackupVerifiedKey);
        return string.IsNullOrEmpty(raw) ? null : DateTimeOffset.Parse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public Task MarkBackupVerifiedAsync()
        => configProvider.Set(BackupVerifiedKey, DateTimeOffset.UtcNow.ToString("O"), backup: false);

    public IReadOnlyList<int> ChooseVerificationPositions(string mnemonic, int count = 4)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
            throw new ArgumentException("Mnemonic is empty", nameof(mnemonic));

        var wordCount = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (count <= 0 || count > wordCount)
            throw new ArgumentOutOfRangeException(nameof(count));

        var picks = new HashSet<int>();
        while (picks.Count < count)
        {
            // 1-based, inclusive of wordCount.
            picks.Add(RandomNumberGenerator.GetInt32(1, wordCount + 1));
        }
        return picks.OrderBy(p => p).ToList();
    }
}
