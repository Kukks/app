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
/// seed never leaves the device.
/// </summary>
public class ArkSignerService(
    ConfigProvider configProvider,
    IWalletProvider walletProvider)
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
        return await signer.GetPubKey(descriptor, cancellationToken);
    }

    /// <summary>
    /// Produces a MuSig2 partial signature on the owner wallet.
    /// </summary>
    public async Task<MusigPartialSignature> SignMusigAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        MusigPrivNonce nonce,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        return await signer.SignMusig(descriptor, context, nonce, cancellationToken);
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
        return await signer.Sign(descriptor, hash, cancellationToken);
    }

    /// <summary>
    /// Generates a secret MuSig2 nonce on the owner wallet for the supplied context.
    /// </summary>
    public async Task<MusigPrivNonce> GenerateNoncesAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        CancellationToken cancellationToken = default)
    {
        var signer = await ResolveSignerAsync(walletId, cancellationToken);
        return await signer.GenerateNonces(descriptor, context, cancellationToken);
    }

    private async Task<IArkadeWalletSigner> ResolveSignerAsync(
        string walletId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(walletId))
            throw new ArgumentException("walletId is required", nameof(walletId));

        var ownerWalletId = await configProvider.Get<string>(ArkWalletBootstrapService.WalletIdKey);
        if (string.IsNullOrEmpty(ownerWalletId))
            throw new InvalidOperationException(
                $"Refusing to sign for wallet '{walletId}': the on-device owner wallet has not finished " +
                "registering with NArk yet. Registration runs in the background and needs the Arkade " +
                "server reachable — retry shortly.");

        if (!string.Equals(ownerWalletId, walletId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Refusing to sign for wallet '{walletId}' — owner wallet on this device is '{ownerWalletId}'.");

        var signer = await walletProvider.GetSignerAsync(ownerWalletId, cancellationToken);
        if (signer is null)
            throw new InvalidOperationException(
                $"Owner wallet '{ownerWalletId}' is not yet ready to sign (no local signer resolved). " +
                "This typically means the wallet record has not landed in NArk storage yet; retry shortly.");

        return signer;
    }
}
