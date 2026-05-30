using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.Core.BTCPayServer;
using BTCPayServer.Plugins.ArkPayServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
/// Dispatch model: walks the currently-connected <em>master</em> devices known
/// to <see cref="BTCPayAppState"/> and tries each one until the device-side
/// validation accepts the <paramref name="walletId"/>. The device throws an
/// <see cref="InvalidOperationException"/> whose message starts with
/// <c>"Refusing to sign for wallet"</c> when the requested wallet id does not
/// match its locally-registered owner wallet — that's the per-device filter.
/// <see cref="KnowsWalletAsync"/> uses the same walk, but probes the cheap
/// <c>KnowsWallet</c> hub method instead of invoking a signing op.
/// </para>
/// <para>
/// <b>MuSig2 session pinning.</b> Per NArk PR #113, the secret nonce that
/// <see cref="GenerateNoncesAsync"/> generates is retained on the signing device
/// indexed by the caller-supplied <paramref name="sessionId"/>, and
/// <see cref="SignMusigAsync"/> consumes it under the same id. If a redundant
/// signer setup (e.g. two phones with the same mnemonic, "hot standby" model)
/// lets the two calls land on different devices, <c>SignMusig</c> on the second
/// device throws because it has no record of the secret nonce — and worse, if
/// both devices independently generated a nonce for the same session,
/// completing the signature would leak the private key (MuSig2 nonce reuse).
/// So when <see cref="GenerateNoncesAsync"/> succeeds we record which connection
/// produced the nonce, and <see cref="SignMusigAsync"/> routes back to that
/// exact connection — or fails clearly if it has gone away. Schnorr-only paths
/// (<see cref="GetPubKeyAsync"/> / <see cref="SignAsync"/>) are stateless and
/// still walk every master device.
/// </para>
/// <para>
/// Failure modes the merchant should expect with hot standby:
/// <list type="bullet">
/// <item>If the pinned device disconnects between <c>GenerateNonces</c> and
///   <c>SignMusig</c>, the round fails. The next batch round starts a fresh
///   <c>GenerateNonces</c> on whichever device answers first — that's the
///   "warm spare takes over for the next session" semantic.</item>
/// <item>If the session pin TTL (<see cref="SessionPinTtl"/>) elapses before
///   <c>SignMusig</c>, the round fails. Generous default (10 min) covers
///   typical batch durations.</item>
/// </list>
/// </para>
/// <para>
/// Pure watch-only wallets never hit this code path: per NArk master,
/// <see cref="NArk.Abstractions.Wallets.IWalletProvider"/> only wraps the
/// transport for a wallet when <see cref="KnowsWalletAsync"/> returns true,
/// so a wallet with no local secret AND no remote device that claims it
/// resolves to a null signer (watch-only).
/// </para>
/// <para>
/// Multi-tenant deployments (multiple BTCPay users each with a paired device
/// signing for distinct Arkade wallets) need an explicit enrolment table —
/// that's deliberately out of scope here.
/// </para>
/// </remarks>
internal sealed class BTCPayAppDeviceProxy : IBTCPayAppDeviceProxy
{
    /// <summary>
    /// How long a MuSig2 session pin survives without being consumed. A pin is
    /// recorded on <c>GenerateNonces</c> success and removed on
    /// <c>SignMusig</c> completion (success OR failure — the device-side nonce
    /// store consumes the nonce on attempted use). The TTL is the safety net
    /// for "GenerateNonces succeeded but SignMusig never came" — entries are
    /// evicted lazily on next access past the cutoff.
    /// </summary>
    private static readonly TimeSpan SessionPinTtl = TimeSpan.FromMinutes(10);

    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly ILogger<BTCPayAppDeviceProxy> _logger;

    // (walletId, sessionId) -> (connectionId, recordedAt). Populated by
    // GenerateNoncesAsync, consumed by SignMusigAsync. ConcurrentDictionary
    // (rather than IMemoryCache) so we own the eviction semantics directly:
    // expired entries are pruned lazily on insert/lookup.
    private readonly ConcurrentDictionary<(string WalletId, string SessionId), SessionPin> _sessionPins = new();

    public BTCPayAppDeviceProxy(
        IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
        BTCPayAppState appState,
        ILogger<BTCPayAppDeviceProxy> logger)
    {
        _hubContext = hubContext;
        _appState = appState;
        _logger = logger;
    }

    public async Task<bool> KnowsWalletAsync(string walletId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(walletId)) return false;

        foreach (var connectionId in MasterConnectionIds())
        {
            try
            {
                var client = _hubContext.Clients.Client(connectionId);
                if (await client.KnowsWallet(walletId)) return true;
            }
            catch
            {
                // Connection dropped or device errored — try the next one. We
                // never want a transient hub failure to make the wallet provider
                // fall back to "watch-only" for a wallet the device actually owns.
            }
        }
        return false;
    }

    public Task<ECPubKey> GetPubKeyAsync(
        string walletId,
        OutputDescriptor descriptor,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.GetPubKey(walletId, descriptor));

    public Task<(ECXOnlyPubKey, SecpSchnorrSignature)> SignAsync(
        string walletId,
        OutputDescriptor descriptor,
        uint256 hash,
        CancellationToken cancellationToken = default)
        => ForwardAsync(walletId, client => client.Sign(walletId, descriptor, hash));

    public async Task<MusigPubNonce> GenerateNoncesAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("sessionId is required for MuSig2 nonce generation", nameof(sessionId));

        var (connectionId, nonce) = await ForwardAndRecordAsync(
            walletId,
            client => client.GenerateNonces(walletId, descriptor, context, sessionId));

        // Pin the session to the connection that produced (and now holds) the
        // secret nonce. SignMusigAsync routes back to this exact connection.
        PrunePinsOlderThan(DateTimeOffset.UtcNow - SessionPinTtl);
        _sessionPins[(walletId, sessionId)] = new SessionPin(connectionId, DateTimeOffset.UtcNow);

        return nonce;
    }

    public async Task<MusigPartialSignature> SignMusigAsync(
        string walletId,
        OutputDescriptor descriptor,
        MusigContext context,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(walletId))
            throw new ArgumentException("walletId is required", nameof(walletId));
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("sessionId is required for MuSig2 signing", nameof(sessionId));

        if (!_sessionPins.TryRemove((walletId, sessionId), out var pin))
            throw new InvalidOperationException(
                $"No device pin for MuSig2 session '{sessionId}' on wallet '{walletId}'. " +
                "Either GenerateNonces was never called for this session, or its pin has expired. " +
                "Retry the batch round.");

        // Pin TTL check (defensive — PrunePinsOlderThan above is best-effort).
        var age = DateTimeOffset.UtcNow - pin.RecordedAt;
        if (age > SessionPinTtl)
            throw new InvalidOperationException(
                $"MuSig2 session pin for '{sessionId}' expired after {age.TotalMinutes:F1} minutes. " +
                "Retry the batch round to generate a fresh nonce.");

        // The pinned connection must still be a connected master. If it has
        // dropped, we MUST fail here — falling through to the other master
        // would ask a device that has no record of the secret nonce, OR worse
        // a device that independently generated its own nonce for the same
        // session (nonce reuse → key leak).
        if (!_appState.Connections.TryGetValue(pin.ConnectionId, out var meta) || !meta.Master)
        {
            _logger.LogWarning(
                "MuSig2 session '{SessionId}' for wallet '{WalletId}' was pinned to connection " +
                "'{ConnectionId}' which has since disconnected. Failing the round; next batch will " +
                "pin to whichever master device answers GenerateNonces first.",
                sessionId, walletId, pin.ConnectionId);

            throw new InvalidOperationException(
                $"The BTCPayApp device that generated the MuSig2 nonce for session '{sessionId}' has " +
                "disconnected before the batch round could complete. The signing nonce is lost. " +
                "The next batch round will pick a new device — open the BTCPayApp and reconnect, then retry.");
        }

        var client = _hubContext.Clients.Client(pin.ConnectionId);
        return await client.SignMusig(walletId, descriptor, context, sessionId);
    }

    private System.Collections.Generic.List<string> MasterConnectionIds()
        => _appState.Connections
            .Where(kv => kv.Value.Master)
            .Select(kv => kv.Key)
            .ToList();

    private async Task<T> ForwardAsync<T>(string walletId, Func<IBTCPayAppHubClient, Task<T>> call)
    {
        var (_, result) = await ForwardAndRecordAsync(walletId, call);
        return result;
    }

    private async Task<(string ConnectionId, T Result)> ForwardAndRecordAsync<T>(
        string walletId,
        Func<IBTCPayAppHubClient, Task<T>> call)
    {
        if (string.IsNullOrEmpty(walletId))
            throw new ArgumentException("walletId is required", nameof(walletId));

        // Snapshot the currently-connected master devices.
        var connectionIds = MasterConnectionIds();

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
                var result = await call(client);
                return (connectionId, result);
            }
            catch (Exception ex) when (IsWalletMismatch(ex))
            {
                // Device-side validation: this connection owns a different
                // wallet. Carry on to the next master connection.
                lastError = ex;
            }
        }

        // Every connected master device declined: surface the last (most
        // recent) device-side error so the merchant sees the actual reason.
        throw new InvalidOperationException(
            $"No connected BTCPayApp device owns wallet '{walletId}'. " +
            "Pair the device that holds this wallet's seed and retry. " +
            $"Last device response: {lastError?.Message ?? "(none)"}",
            lastError);
    }

    private void PrunePinsOlderThan(DateTimeOffset cutoff)
    {
        foreach (var entry in _sessionPins)
        {
            if (entry.Value.RecordedAt < cutoff)
                _sessionPins.TryRemove(entry.Key, out _);
        }
    }

    // The on-device ArkSignerService throws InvalidOperationException with a
    // message that starts with "Refusing to sign for wallet" when the requested
    // wallet id doesn't match the device's owner wallet. Match on that prefix
    // so we don't swallow unrelated InvalidOperationExceptions (e.g. a real
    // signing failure on the right device).
    private static bool IsWalletMismatch(Exception ex)
        => ex is InvalidOperationException
           && ex.Message.StartsWith("Refusing to sign for wallet", StringComparison.Ordinal);

    private sealed record SessionPin(string ConnectionId, DateTimeOffset RecordedAt);
}
