#!/usr/bin/env bash

if [ ! -v CI ]; then
  # Initialize the server submodule
  git submodule init && git submodule update --recursive

  # Install the workloads
  dotnet workload restore
fi

# Create appsettings file to include app plugin when running the server
appsettings="submodules/btcpayserver/BTCPayServer/appsettings.dev.json"
if [ ! -f $appsettings ]; then
    echo '{ "DEBUG_PLUGINS": "../../../BTCPayServer.Plugins.App/bin/Debug/net10.0/BTCPayServer.Plugins.App.dll;../../../submodules/btcpay-arkade/BTCPayServer.Plugins.ArkPayServer/bin/Debug/net10.0/BTCPayServer.Plugins.ArkPayServer.dll" }' > $appsettings
fi

# Publish plugins to share their dependencies with the server.
# The Arkade plugin publishes separately (the App plugin declares it as a
# plugin dependency and links to its load context at runtime — it does not
# ship the Arkade DLLs itself).
cd BTCPayServer.Plugins.App
dotnet publish -c Debug -o bin/Debug/net10.0
cd -
cd submodules/btcpay-arkade/BTCPayServer.Plugins.ArkPayServer
dotnet publish -c Debug -o bin/Debug/net10.0
cd -

# .NET type identity is per load context: every assembly on the App↔Arkade
# signer seam (NArk.*, NBitcoin.Secp256k1) must resolve in exactly ONE plugin
# load context or the cross-plugin IRemoteSignerTransport implementation fails
# to type-load ("method does not have an implementation"). Host-owned
# assemblies are shared from the host (PreferSharedTypes); Arkade-owned ones
# stay only in the Arkade plugin folder; the App plugin reaches them through
# its declared plugin dependency (PluginManager links the load contexts). So:
# prune host-owned DLLs from the Arkade folder, then prune host-owned and
# Arkade-owned DLLs from the App folder.
host_bin="submodules/btcpayserver/BTCPayServer/bin/Debug/net10.0"
ark_bin="submodules/btcpay-arkade/BTCPayServer.Plugins.ArkPayServer/bin/Debug/net10.0"
app_bin="BTCPayServer.Plugins.App/bin/Debug/net10.0"
dotnet build submodules/btcpayserver/BTCPayServer -c Debug
for dll in "$ark_bin"/*.dll; do
  name="$(basename "$dll")"
  if [ "$name" != "BTCPayServer.Plugins.ArkPayServer.dll" ] && [ -f "$host_bin/$name" ]; then
    rm "$dll"
  fi
done
for dll in "$app_bin"/*.dll; do
  name="$(basename "$dll")"
  if [ "$name" != "BTCPayServer.Plugins.App.dll" ] && { [ -f "$host_bin/$name" ] || [ -f "$ark_bin/$name" ]; }; then
    rm "$dll"
  fi
done
