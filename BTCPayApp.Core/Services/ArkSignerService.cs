using BTCPayApp.Core.Contracts;
using NArk.Abstractions.Wallets;
using NBitcoin;
using NBitcoin.Scripting;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;

namespace BTCPayApp.Core.Services;

/// <summary>
/// On-device signer bridge that fulfils <see cref="IRemoteSignerTransport"/>-shaped
/// requests coming from the BTCPayServer-side companion plugin over the SignalR
/// hub. Each call:
/// <list type="number">
/// <item>verifies the requested <c>walletId</c> matches the owner wallet that
/// <see cref="ArkWalletBootstrapService"/> registered on this device — defence
/// in depth so a compromised or buggy server cannot ask us to sign for a wallet
/// we don't own;</item>
/// <item>resolves the local <see cref="IArkadeWalletSigner"/> through
/// <see cref="IWalletProvider.GetSignerAsync"/> (backed by the locally-stored
/// BIP-39 seed);</item>
/// <item>delegates the cryptographic operation to that signer.</item>
/// </list>
/// The local signer is the only place the on-device seed is ever exercised; the
/// seed never leaves the device. After every successful signing op we bump the
/// <see cref="SignerStatusService"/> heartbeat so the badge surfaces "Last
/// signed"; refusals record the failure message so the merchant sees why.
/// </summary>
public class ArkSignerService(
    ConfigProvider configProvider,
    IWalletProvider walletProvider,
    SignerStatusService statusService)
{
    /// <summary>
    /// Gets the compressed public key for the given descriptor on the owner wallet.
    /// </summary>
    public async Task<ECPubKey> GetPubKeyAsync(
        string walletId,
        OutputDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        var result = await signer.GetPubKey(descriptor, cancellationToken);
        statusService.RecordSign();
        return result;
    }

    /// <summary>
    /// Produces a MuSig2 partial signature on the owner wallet. The local signer
    /// looks up the secret nonce by <paramref name="sessionId"/> from the store
    /// populated by a prior <see cref="GenerateNoncesAsync"/> call — the secret
    /// half never crossed the SignalR boundary.
    /// </summary>
    public async Task<MusigPartialSignature> SignMusigAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        var result = await signer.SignMusig(descriptor, context, sessionId, cancellationToken);
        statusService.RecordSign();
        return result;
    }

    /// <summary>
    /// Produces a BIP-340 Schnorr signature over <paramref name="hash"/> on the
    /// owner wallet.
    /// </summary>
    public async Task<(ECXOnlyPubKey, SecpSchnorrSignature)> SignAsync(
        string walletId,
        OutputDescriptor descriptor,
        uint256 hash,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        var result = await signer.Sign(descriptor, hash, cancellationToken);
        statusService.RecordSign();
        return result;
    }

    /// <summary>
    /// Generates a MuSig2 nonce pair on the owner wallet for the supplied
    /// context, retains the secret half on-device indexed by
    /// <paramref name="sessionId"/>, and returns the public half over the wire.
    /// </summary>
    public async Task<MusigPubNonce> GenerateNoncesAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        var result = await signer.GenerateNonces(descriptor, context, sessionId, cancellationToken);
        statusService.RecordSign();
        return result;
    }

    /// <summary>
    /// Indicates whether this device owns the given <paramref name="walletId"/>.
    /// Returns <c>true</c> when the registered owner wallet matches — that's the
    /// signal the server-side proxy uses to route a sign call to the right
    /// device without a separate enrolment table. Lightweight: does NOT resolve
    /// the local signer (no NArk storage hit), only checks
    /// <see cref="ArkWalletBootstrapService.WalletIdKey"/>.
    /// </summary>
    public async Task<bool> KnowsWalletAsync(string walletId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(walletId)) return false;
        var ownerWalletId = await configProvider.Get<string>(ArkWalletBootstrapService.WalletIdKey);
        return !string.IsNullOrEmpty(ownerWalletId)
               && string.Equals(ownerWalletId, walletId, StringComparison.Ordinal);
    }

    private async Task<IArkadeWalletSigner> ResolveSignerAsync(
        string walletId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(walletId))
            throw new ArgumentException("walletId is required", nameof(walletId));

        var ownerWalletId = await configProvider.Get<string>(ArkWalletBootstrapService.WalletIdKey);
        if (string.IsNullOrEmpty(ownerWalletId))
        {
            var msg = $"Refusing to sign for wallet '{walletId}': the on-device owner wallet has not finished " +
                      "registering with NArk yet. Registration runs in the background and needs the Arkade " +
                      "server reachable — retry shortly.";
            statusService.RecordError(msg);
            throw new InvalidOperationException(msg);
        }

        if (!string.Equals(ownerWalletId, walletId, StringComparison.Ordinal))
        {
            var msg = $"Refusing to sign for wallet '{walletId}' — owner wallet on this device is '{ownerWalletId}'.";
            statusService.RecordError(msg);
            throw new InvalidOperationException(msg);
        }

        var signer = await walletProvider.GetSignerAsync(ownerWalletId, cancellationToken);
        if (signer is null)
        {
            var msg = $"Owner wallet '{ownerWalletId}' is not yet ready to sign (no local signer resolved). " +
                      "This typically means the wallet record has not landed in NArk storage yet; retry shortly.";
            statusService.RecordError(msg);
            throw new InvalidOperationException(msg);
        }

        return signer;
    }
}
