# BTCPay App

A cross-platform point-of-sale app for [BTCPay Server](https://github.com/btcpayserver/btcpayserver), built on [Arkade](https://docs.arkadeos.com/).
The device holds the Arkade owner wallet and is the signer — key material never leaves it.
The paired BTCPay Server store runs a watch-only wallet and forwards anything that needs a signature to the app over SignalR.
See [Fundamentals.md](Fundamentals.md) for the architecture in detail.

## Setup for development

Here's what needs to happen to run the app in the browser:

```bash
# Clone the app repo
git clone git@github.com:btcpayserver/app.git

# Switch to it
cd app

# Run the setup script (on Windows: ./setup.ps1)
./setup.sh

# Go to the server submodule
cd submodules/btcpayserver/BTCPayServer.Tests

# Run the server dependencies
docker-compose up dev
```

The setup script initializes the submodules, publishes the two server plugins — the App plugin (`BTCPayServer.Plugins.App`) and the Arkade plugin (`submodules/btcpay-arkade`, `BTCPayServer.Plugins.ArkPayServer`) — and writes both to `DEBUG_PLUGINS` in `submodules/btcpayserver/BTCPayServer/appsettings.dev.json`.
It then prunes the plugin bin folders so that each assembly on the App↔Arkade signer seam (`NArk.*`, `NBitcoin.Secp256k1`) lives in exactly one plugin load context — re-run it whenever plugin dependencies change.

For payments to work, BTCPay Server must run against the same regtest `bitcoind`/`nbxplorer` as the `arkd` operator, so that the store and the operator see the same chain.

Now you can open up the IDE and run the `DEV ALL` profile which builds both the App and the BTCPay Server.
The development head is `BTCPayApp.Server` (Blazor Server, `http` profile, http://localhost:5259); the packaged heads are `BTCPayApp.Photino` (Windows desktop) and `BTCPayApp.Maui` (Android/iOS).

The app should open in the browser and you should see the Welcome screen.
Click the Connect button, use `https://localhost:14142` as the server URL and register or log in with a server account.
On first run the app generates its Arkade owner wallet automatically, and the network is inherited from the paired server (before pairing it defaults to mutinynet).

## Pairing a store wallet

The store's wallet is watch-only — the app does the signing:

- App: Go to the [Signer status view](http://localhost:5259/wallet/signer) and copy the account descriptor shown as "Owner wallet id"
- Server: In the store's Arkade wallet initial setup, choose "Pair a watch-only wallet" and paste the descriptor — the server probes the connected device to confirm it knows the wallet
- Server: Create a Point of Sale charge; the invoice presents an Arkade address (plus a boarding address)
- Customer: Pays an Arkade transaction and the invoice settles

Back up the seed via the [backup view](http://localhost:5259/wallet/backup).
On mainnet the POS stays gated until the backup is verified, the hub is connected and the owner wallet is registered; other networks are not gated.

## Tests

```bash
dotnet test BTCPayApp.Tests
```

`CanStartAppCore` requires a running BTCPay Server, reachable at `BTCPAY_SERVER_URL` (default: `https://localhost:14142`).

## Troubleshooting

### Development certificates

After the first run of `DEV ALL` on a Linux machine with a new .NET setup, you may run into the [dotnet dev-certs - Untrusted Root](https://github.com/dotnet/aspnetcore/issues/41503)
error, and you may find a solution at the [following link](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)

### GrapheneOS

If you are using GrapheneOS for the Android development, make sure to explicitely [enable code debugging](https://discuss.grapheneos.org/d/8330-app-compatibility-with-grapheneos).
To run the app with debugger attached, the BTCPay app needs to get explicitely set as debug app in `Settings > System > Developer Settings > Debugging > Set Debug App`.

### Sunmi V2s

To enable developer mode on the POS device, go to `Settings > About device` and tap the `Build number` list item seven times. It will conmfirm "You are now a developer" and afterwards you will find `Settings > System > Developer options` being present. There you can turn on USB debugging and select BTCPay app for debugging.
