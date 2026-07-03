# BTCPay ~~Server~~ App

## Introduction

BTCPay Server is an incredibly successful self-hosted, free, open-source payment processor for Bitcoin.
It allows anyone to install it on a server and start accepting payments with no middlemen.
Setting up a bitcoin payment method is relatively simple: you import an existing wallet or generate a new one, and there is absolutely no need to expose your private keys to the server.
The server only watches the chain and verifies payments — it cannot spend.
This watch-only model is what enabled BTCPay Server to become a multi-merchant, multi-store solution with minimal trust required.

But plain on-chain payments are not very feasible for many commerce use-cases: confirmations are slow and fees are unpredictable.
For years the answer was the Lightning Network, and a previous incarnation of this app ran an entire Lightning node on the device — an LDK-based node with channels, liquidity management, versioned state backups and a master/slave election between paired devices.
That architecture is gone.
Lightning's requirements — hot keys that must be constantly online, channel state that must never fork, backups that must capture every state update — were fundamentally at odds with the watch-only trust model that made BTCPay's on-chain support so successful.

The app is now built on [Arkade](https://docs.arkadeos.com/).
Arkade gives us instant payments while restoring the property we never wanted to give up: the server holds no keys, and the whole wallet is recoverable from a single BIP-39 mnemonic.

## What Arkade changes

With Arkade, funds are controlled by an owner wallet whose seed lives only on the merchant's device.
Customers pay to an Arkade address, payments arrive as Arkade transactions, and the operator periodically anchors settlement on-chain in batches via commitment transactions.
From the merchant's perspective there are no channels to open, no liquidity to manage, and no state to continuously back up — the mnemonic is the backup.

The trust split mirrors BTCPay's on-chain model:

* The **device** is the Arkade owner wallet and the signer. Key material is generated on the device and never leaves it.
* The **server** is watch-only. It constructs and tracks Arkade transactions for the store, and whenever a signature is needed it asks a paired device over a real-time connection.

## The user interface

The user interface is designed to be simple and intuitive, with a focus on the most common operations a merchant would need to perform in-person.
It is built using Blazor, which allows us to build re-usable components shared across all platforms and BTCPay Server itself.
The host application is Photino for desktop, MAUI for mobile (Android/iOS), and Blazor Server for web access and development.
Onboarding is meant to be as smooth as possible: shared-server hosts can direct users to the application through invitation links that install and automatically configure everything needed to start accepting payments.

## The owner wallet (device)

`ArkWalletBootstrapService` (in `BTCPayApp.Core/Services`) stands up the owner wallet:

* On first run it generates a BIP-39 mnemonic and persists it locally via `ConfigProvider` under the `ark:owner:mnemonic` key. The key is stored with backup disabled — the seed is never synced to the server and never leaves the device.
* It registers the wallet with the NArk SDK's `IWalletStorage` as an HD wallet (BIP-86 wildcard account descriptor), built exactly as the SDK's `WalletFactory` builds it. The resulting wallet id is tracked under `ark:owner:walletid`.
* Registration needs to know the network (to pick the BIP-86 coin type), which comes from the paired server. It therefore runs off the startup path on a 10-second retry loop — an unreachable server can never block or crash app startup.

The service is idempotent: on subsequent starts the existing mnemonic is reused and the wallet is only re-registered if NArk storage has lost it.

## The signer (device)

`ArkSignerService` is the only component that touches the seed for signing.
It validates that every request targets the owner wallet — a request for any other wallet id is refused — and then exposes a small signing surface:

* `KnowsWallet` — does this device hold the wallet with the given id?
* `GetPubKey` — derive a public key for a descriptor.
* `Sign` — produce a Schnorr signature.
* `SignMusig` / `GenerateNonces` — the two halves of MuSig2 signing, correlated by a session id.

These methods are exposed to the server over the SignalR hub via `BTCPayAppServerClient`: the server asks, the device answers, and private keys never move.

## The server side: watch-only by design

On the server, two plugins cooperate:

* `BTCPayServer.Plugins.App` (this repository) hosts the SignalR hub at `hub/btcpayapp` and everything device-facing.
* `BTCPayServer.Plugins.ArkPayServer` (the Arkade plugin, `submodules/btcpay-arkade`) provides the Arkade wallet, payment method and operator integration. It is a declared plugin dependency of the App plugin.

The bridge between them is `BTCPayAppDeviceProxy`, the App plugin's implementation of the Arkade plugin's `IBTCPayAppDeviceProxy : IRemoteSignerTransport` contract.
When the Arkade plugin needs a signature, the proxy forwards the call to a connected master device over the hub.
When a store pairs a watch-only wallet by account descriptor, the server probes connected devices with `KnowsWallet` to confirm one of them actually holds it.

The store wallet itself never gains signing capability: the server can derive addresses, watch for payments and assemble transactions, but the final say is always the device's.

### Plugin load contexts

Because the signer contract crosses a plugin boundary, .NET assembly identity matters: every assembly on the App↔Arkade seam (`NArk.*`, `NBitcoin.Secp256k1`) must resolve in exactly one plugin load context, or the cross-plugin `IRemoteSignerTransport` implementation fails to type-load.
Host-owned assemblies are shared from the host; Arkade-owned ones live only in the Arkade plugin's folder, and the App plugin reaches them through its declared plugin dependency.
This is why the setup script publishes both plugins and then prunes duplicate DLLs from their bin folders — see the repository README.

## MuSig2 nonce sessions

MuSig2 signing happens in two steps — generate nonces, then sign — and its security depends on the secret nonce: it must stay wherever it was generated, and it must never be used twice.
Reusing a MuSig2 nonce leaks the private key.

The device proxy therefore pins every nonce session to the exact SignalR connection that created it.
`GenerateNonces` records a pin keyed by (wallet id, session id) pointing at the connection id that answered; `SignMusig` for that session consumes the pin and must be served by that same connection.
If the pinned device has disconnected, or a different device tries to complete the session, the call fails and the flow restarts with fresh nonces.
Pins are single-use and expire after a short window.

The consequence is that the secret nonce never crosses the wire, and no combination of reconnects or standby devices can trick the system into completing a session with a mismatched or reused nonce.

## Configuration sync

The device does not choose its own network — it inherits it from the paired BTCPay store.
`ArkadeConfigSyncService` listens for the hub's Connected event, calls `GetArkadeConfig()` on the server and persists the resulting `ArkNetworkConfig` locally.
Before any server is paired, `ArkConfiguration.Resolve` falls back to a default network (mutinynet); mainnet, mutinynet (signet) and regtest are resolvable by name.

This means a device paired to a regtest store behaves as a regtest wallet, and re-pairing against a mainnet store switches it — the server dictates, the device follows.

## Signer health and mainnet preflight

Because the store depends on the device for every signature, the device's health is a first-class surface.
`SignerStatusService` condenses it into three levels:

* **Unsafe** — no mnemonic or no registered owner wallet; the device cannot sign at all.
* **Degraded** — the hub is disconnected or the mnemonic backup has not been verified.
* **Healthy** — wallet registered, hub connected, backup verified.

The status (plus network, owner wallet id, last signature and last error) is shown at `/wallet/signer` in the app.

`MainnetPreflightService` turns this into a hard gate: on mainnet, the point-of-sale flow is blocked until the mnemonic is backed up and verified, the device is paired with the hub connected, and the owner wallet is registered with the operator.
On any other network the preflight reports "not applicable" so testnet and regtest demos are never gated.

## Backup and recovery

The backup is the mnemonic — nothing else.
There is no channel state, no versioned outbox, no encrypted state sync to the server: the VSS-style backup machinery of the Lightning era has no equivalent here because nothing beyond the seed needs backing up.

`MnemonicBackupService` drives a manual backup-and-verify flow at `/wallet/backup` (with verification at `/wallet/backup/verify`).
Until the user proves they have written the words down, the signer reports Degraded and the mainnet preflight fails.

Recovery lives at `/wallet/recover`: entering a mnemonic replaces the local seed and clears the tracked wallet id, so the bootstrap service re-registers the recovered wallet on its next pass.

## Hot standby

Recovery doubles as a hot-standby mechanism: a second device can be paired against the same Arkade owner wallet by recovering the same mnemonic on it.
Both devices then register the same wallet, and the server forwards signing to whichever one is connected.

In the Lightning era this was catastrophic — two nodes sharing channel state would fork it and lose funds, which is why the old architecture needed a master/slave election.
With Arkade there is no state to fork, only keys, and the one genuinely dangerous shared-state operation — MuSig2 nonce sessions — is made safe by connection pinning: a session started on one device can only ever be completed by that device.

## Connectivity

The app maintains a single real-time SignalR connection to the server's `hub/btcpayapp` endpoint, established by the connection manager against the paired account's base URI.
Everything device-facing flows over it in both directions: configuration sync, the `KnowsWallet` pairing probe, and every signing call the store wallet delegates.
The connection state feeds directly into the signer health level — a disconnected hub means a Degraded signer, because the store cannot reach its signer at all.

## The payment flow

End to end, a merchant goes live like this:

1. Pair the app with a BTCPay Server: Connect, enter the server URL, register or log in.
2. The device bootstraps its owner wallet automatically and inherits the server's network.
3. In BTCPay, the store's Arkade wallet initial setup offers "Pair a watch-only wallet"; the merchant pastes the device's account descriptor (shown at `/wallet/signer`).
4. The server verifies a connected device knows the wallet, and the store wallet is created watch-only with signing delegated to the device.
5. A point-of-sale charge produces an invoice with an Arkade address (plus a boarding address for on-chain funds).
6. The customer pays an Arkade transaction and the invoice settles.

## What is not here yet

* **Multi-tenant pairing** — the current model is one store, one user, one device.
* **Unilateral exit UX** — there is no in-app flow for exiting unilaterally from the operator.
* **Lightning** — only what the Arkade plugin surfaces through Boltz swaps; the app itself has no other Lightning flows.
