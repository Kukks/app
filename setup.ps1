# Check if not in a CI environment
if (-not (Test-Path Env:CI)) {
    # Initialize the server submodule
    Write-Host "Initializing and updating submodules..."
    git submodule init
    if ($LASTEXITCODE -eq 0) {
        git submodule update --recursive
    } else {
        Write-Error "git submodule init failed."
        exit 1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "git submodule update --recursive failed."
        exit 1
    }

    # Install the workloads
    Write-Host "Restoring dotnet workloads..."
    dotnet workload restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet workload restore failed."
        exit 1
    }
}

# Create appsettings file to include app plugin when running the server
$appsettings = "submodules/btcpayserver/BTCPayServer/appsettings.dev.json"
if (-not (Test-Path $appsettings -PathType Leaf)) {
    Write-Host "Creating $appsettings..."
    $content = '{ "DEBUG_PLUGINS": "../../../BTCPayServer.Plugins.App/bin/Debug/net10.0/BTCPayServer.Plugins.App.dll;../../../submodules/btcpay-arkade/BTCPayServer.Plugins.ArkPayServer/bin/Debug/net10.0/BTCPayServer.Plugins.ArkPayServer.dll" }'
    Set-Content -Path $appsettings -Value $content -Encoding UTF8
}

# Publish plugin to share its dependencies with the server
$originalLocation = Get-Location
$pluginDir = "BTCPayServer.Plugins.App"

if (Test-Path $pluginDir) {
    Write-Host "Changing directory to $pluginDir..."
    Set-Location $pluginDir

    Write-Host "Publishing plugin..."
    dotnet publish -c Debug -o "bin/Debug/net10.0"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed."
        Set-Location $originalLocation # Ensure we return to original location on error
        exit 1
    }

    Write-Host "Returning to original directory..."
    Set-Location $originalLocation
} else {
    Write-Error "Plugin directory $pluginDir not found."
    exit 1
}

# Publish the Arkade plugin separately: the App plugin declares it as a plugin
# dependency and links to its load context at runtime — it does not ship the
# Arkade DLLs itself.
$arkPluginDir = "submodules/btcpay-arkade/BTCPayServer.Plugins.ArkPayServer"
if (Test-Path $arkPluginDir) {
    Write-Host "Publishing Arkade plugin..."
    Set-Location $arkPluginDir
    dotnet publish -c Debug -o "bin/Debug/net10.0"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed for Arkade plugin."
        Set-Location $originalLocation
        exit 1
    }
    Set-Location $originalLocation
} else {
    Write-Error "Arkade plugin directory $arkPluginDir not found."
    exit 1
}

# .NET type identity is per load context: every assembly on the App↔Arkade
# signer seam (NArk.*, NBitcoin.Secp256k1) must resolve in exactly ONE plugin
# load context or the cross-plugin IRemoteSignerTransport implementation fails
# to type-load ("method does not have an implementation"). Host-owned
# assemblies are shared from the host (PreferSharedTypes); Arkade-owned ones
# stay only in the Arkade plugin folder; the App plugin reaches them through
# its declared plugin dependency (PluginManager links the load contexts). So:
# prune host-owned DLLs from the Arkade folder, then prune host-owned and
# Arkade-owned DLLs from the App folder.
$hostBin = "submodules/btcpayserver/BTCPayServer/bin/Debug/net10.0"
$arkBin = "$arkPluginDir/bin/Debug/net10.0"
$appBin = "BTCPayServer.Plugins.App/bin/Debug/net10.0"
dotnet build submodules/btcpayserver/BTCPayServer -c Debug
$hostNames = (Get-ChildItem $hostBin -Filter *.dll).Name
Get-ChildItem $arkBin -Filter *.dll | Where-Object { $_.Name -ne 'BTCPayServer.Plugins.ArkPayServer.dll' -and $hostNames -contains $_.Name } | Remove-Item -Force
$arkNames = (Get-ChildItem $arkBin -Filter *.dll).Name
Get-ChildItem $appBin -Filter *.dll | Where-Object { $_.Name -ne 'BTCPayServer.Plugins.App.dll' -and ($hostNames -contains $_.Name -or $arkNames -contains $_.Name) } | Remove-Item -Force

Write-Host "Setup complete."
