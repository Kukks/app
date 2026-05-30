using System.Security.Cryptography;
using BTCPayApp.Core.Contracts;
using NBitcoin;

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

    /// <summary>
    /// Replaces the on-device wallet seed with the supplied mnemonic, used to
    /// pair a second device against the same Arkade owner wallet (hot-standby
    /// redundancy). The current <see cref="ArkWalletBootstrapService.WalletIdKey"/>
    /// is cleared so the bootstrap service re-registers the recovered wallet
    /// with the operator on its next retry (idempotent — re-registering an
    /// existing walletId is a no-op on the operator side). Marks the backup as
    /// verified, since the merchant just typed the words.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The supplied phrase is empty, has the wrong word count (must be 12 or
    /// 24), or fails BIP-39 checksum validation.
    /// </exception>
    public async Task ReplaceWithRecoveredAsync(string mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
            throw new ArgumentException("Mnemonic is required", nameof(mnemonic));

        // Tolerant input cleanup: collapse whitespace, lowercase. BIP-39 words
        // are case-insensitive in spec but NBitcoin's wordlist match is exact.
        var normalized = string.Join(' ',
            mnemonic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim().ToLowerInvariant()));

        var wordCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is not (12 or 24))
            throw new ArgumentException(
                $"Only 12 or 24-word BIP-39 mnemonics are supported; got {wordCount} word(s).",
                nameof(mnemonic));

        // BIP-39 checksum validation — the Mnemonic ctor throws on bad words
        // (FormatException) and on bad checksum (also FormatException). Catch
        // and re-raise as ArgumentException with the inner message so the UI
        // can show a single error path.
        try
        {
            _ = new Mnemonic(normalized, Wordlist.English);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                $"Not a valid BIP-39 mnemonic: {ex.Message}", nameof(mnemonic), ex);
        }

        await configProvider.Set(ArkWalletBootstrapService.MnemonicKey, normalized, backup: false);

        // Clear the prior wallet id so the bootstrap retry loop picks up the
        // recovered seed and re-registers. Until that lands the SignerStatus
        // badge will briefly read "Unsafe" — that's the expected transition.
        await configProvider.Set<string>(ArkWalletBootstrapService.WalletIdKey, null, backup: false);

        // The merchant just typed the words — they're obviously backed up.
        await MarkBackupVerifiedAsync();
    }
}
