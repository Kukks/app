using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Keeps the on-device <see cref="ArkadeOperatorConfig"/> in sync with the
/// paired BTCPay store's btcpay-arkade plugin configuration. Subscribes to
/// <see cref="BTCPayConnectionManager.ConnectionChanged"/>; whenever the hub
/// reaches <see cref="BTCPayConnectionState.Connected"/>, fetches the server's
/// <see cref="ArkadeServerConfigDto"/> via
/// <see cref="IBTCPayAppHubServer.GetArkadeConfig"/> and persists it locally.
/// </summary>
/// <remarks>
/// Idempotent: only writes when the incoming DTO differs from what's already
/// persisted, so reconnect storms don't churn the settings table. Failures are
/// logged and swallowed — a transient hub error must not bring the host down,
/// and the next reconnect will retry.
/// </remarks>
public class ArkadeConfigSyncService(
    BTCPayConnectionManager connectionManager,
    ArkadeOperatorConfig operatorConfig,
    ILogger<ArkadeConfigSyncService> logger)
    : BaseHostedService(logger)
{
    protected override Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        connectionManager.ConnectionChanged += OnConnectionChanged;
        return Task.CompletedTask;
    }

    protected override Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        connectionManager.ConnectionChanged -= OnConnectionChanged;
        return Task.CompletedTask;
    }

    private async Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        if (e.New != BTCPayConnectionState.Connected)
            return;

        var hub = connectionManager.HubProxy;
        if (hub is null)
        {
            logger.LogDebug("Hub reported connected but proxy is null; skipping Arkade config sync");
            return;
        }

        try
        {
            var incoming = await hub.GetArkadeConfig();
            if (incoming is null)
            {
                logger.LogDebug("Server returned no Arkade config; skipping sync");
                return;
            }

            var current = await operatorConfig.GetServerConfigAsync();
            if (current == incoming)
            {
                logger.LogDebug("Arkade config already in sync ({NetworkType}); skipping write", incoming.NetworkType);
                return;
            }

            await operatorConfig.SetServerConfigAsync(incoming);
            logger.LogInformation("Synced Arkade network config from paired BTCPay store ({NetworkType}, {ArkUri})",
                incoming.NetworkType, incoming.ArkUri);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync Arkade network config from paired BTCPay store; will retry on next reconnect");
        }
    }
}
