namespace BTCPayApp.Core.Models;

public enum SignerHealthLevel { Healthy, Degraded, Unsafe }

public record SignerStatus(
    SignerHealthLevel Level,
    string? OwnerWalletId,
    string? LastError,
    DateTimeOffset? LastSignAt,
    bool HubConnected,
    bool BackupVerified,
    string NetworkName
);
