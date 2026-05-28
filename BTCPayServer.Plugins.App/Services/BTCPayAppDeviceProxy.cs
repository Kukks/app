using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.Core.BTCPayServer;
using BTCPayServer.Plugins.ArkPayServer.Services;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;
using NBitcoin.Scripting;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;

namespace BTCPayServer.Plugins.App.Services;

/// <summary>
/// Server-side bridge that fulfils
/// <see cref="IBTCPayAppDeviceProxy"/> by forwarding each remote-signer call
/// to a connected BTCPayApp device over the existing SignalR hub. The on-device
/// <c>ArkSignerService</c> resolves the local <see cref="NArk.Abstractions.Wallets.IArkadeWalletSigner"/>
/// for the requested <paramref name="walletId"/> and produces the cryptographic
/// material — the seed never leaves the device.
/// </summary>
/// <remarks>
/// <para>
/// Phase-1 dispatch: walks the currently-connected <em>master</em> devices known
/// to <see cref="BTCPayAppState"/> and tries each one until the device-side
/// validation accepts the <paramref name="walletId"/>. The device throws an
/// <see cref="InvalidOperationException"/> whose message starts with
/// <c>"Refusing to sign for wallet"</c> when the requested wallet id does not
/// match its locally-registered owner wallet — that's the per-device filter.
/// </para>
/// <para>
/// This design works for the common single-user / single-device pairing without
/// requiring a server-side <c>walletId → userId</c> mapping. Pure
/// <see cref="NArk.Abstractions.Wallets.WalletType.WatchOnly"/> wallets never hit
/// this code path (NArk's <c>DefaultWalletProvider</c> only wraps the transport
/// for <c>WalletType.Remote</c>); the failure modes that DO reach here are:
/// </para>
/// <list type="bullet">
/// <item>No master device connected — clear "open the app and reconnect" error.</item>
/// <item>One or more devices connected but none owns <paramref name="walletId"/>
///   — surface the last device's error so the merchant sees exactly why.</item>
/// </list>
/// <para>
/// Multi-tenant deployments (multiple BTCPay users each with a paired device
/// signing for distinct Arkade-Remote wallets) need an explicit enrolment
/// table — that's deliberately out of scope here.
/// </para>
/// </remarks>
internal sealed class BTCPayAppDeviceProxy : IBTCPayAppDeviceProxy
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;

    public BTCPayAppDeviceProxy(
        IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
        BTCPayAppState appState)
    {
        _hubContext = hubContext;
        _appState = appState;
    }

    public Task<ECPubKey> GetPubKeyAsync(
        string walletId,
        OutputDescriptor descriptor,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.GetPubKey(walletId, descriptor));

    public Task<MusigPartialSignature> SignMusigAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        MusigPrivNonce nonce,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.SignMusig(walletId, descriptor, context, nonce));

    public Task<(ECXOnlyPubKey, SecpSchnorrSignature)> SignAsync(
        string walletId,
        OutputDescriptor descriptor,
        uint256 hash,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.Sign(walletId, descriptor, hash));

    public Task<MusigPrivNonce> GenerateNoncesAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.GenerateNonces(walletId, descriptor, context));

    private async Task<T> ForwardAsync<T>(string walletId, Func<IBTCPayAppHubClient, Task<T>> call)
    {
        if (string.IsNullOrEmpty(walletId))
            throw new ArgumentException("walletId is required", nameof(walletId));

        // Snapshot the currently-connected master devices. BTCPayAppState
        // enforces at-most-one master per user already, so this is the natural
        // set of "signers that should hold the seed for an Arkade owner wallet".
        var connectionIds = _appState.Connections
            .Where(kv => kv.Value.Master)
            .Select(kv => kv.Key)
            .ToList();

        if (connectionIds.Count == 0)
            throw new InvalidOperationException(
                $"No connected BTCPayApp device available to sign for wallet '{walletId}'. " +
                "Open the BTCPayApp on your paired device and reconnect, then retry.");

        Exception? lastError = null;
        foreach (var connectionId in connectionIds)
        {
            try
            {
                var client = _hubContext.Clients.Client(connectionId);
                return await call(client);
            }
            catch (Exception ex) when (IsWalletMismatch(ex))
            {
                // Device-side validation: this connection owns a different
                // wallet. Carry on to the next master connection — exactly
                // one of them should own walletId.
                lastError = ex;
            }
        }

        // Every connected master device declined: surface the last (most
        // recent) device-side error so the merchant sees the actual reason
        // instead of a generic "not found".
        throw new InvalidOperationException(
            $"No connected BTCPayApp device owns wallet '{walletId}'. " +
            "Pair the device that holds this wallet's seed and retry. " +
            $"Last device response: {lastError?.Message ?? "(none)"}",
            lastError);
    }

    // The on-device ArkSignerService throws InvalidOperationException with a
    // message that starts with "Refusing to sign for wallet" when the requested
    // wallet id doesn't match the device's owner wallet. Match on that prefix
    // so we don't swallow unrelated InvalidOperationExceptions (e.g. a real
    // signing failure on the right device).
    private static bool IsWalletMismatch(Exception ex)
        => ex is InvalidOperationException
           && ex.Message.StartsWith("Refusing to sign for wallet", StringComparison.Ordinal);
}
