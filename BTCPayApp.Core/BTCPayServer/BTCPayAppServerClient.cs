using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.BTCPayServer;


public class BTCPayAppServerClient(ILogger<BTCPayAppServerClient> _logger)
    : IBTCPayAppHubClient
{
    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    public event AsyncEventHandler<string>? OnServerNodeInfo;
    public event AsyncEventHandler<long?>? OnMasterUpdated;
    public event AsyncEventHandler<ServerEvent>? OnNotifyServerEvent;

    public async Task NotifyServerEvent(ServerEvent ev)
    {
        _logger.LogInformation("NotifyServerEvent: {Event}", ev.ToString());
        if (OnNotifyServerEvent is null) return;
        await OnNotifyServerEvent.Invoke(this, ev);
    }

    public async Task NotifyNetwork(string network)
    {
        _logger.LogInformation("NotifyNetwork: {Network}", network);
        if (OnNotifyNetwork is null) return;
        await OnNotifyNetwork.Invoke(this, network);
    }

    public async Task NotifyServerNode(string nodeInfo)
    {
        _logger.LogInformation("NotifyServerNode: {NodeInfo}", nodeInfo);
        if (OnServerNodeInfo is null) return;
        await OnServerNodeInfo.Invoke(this, nodeInfo);
    }

    public async Task TransactionDetected(TransactionDetectedRequest request)
    {
        _logger.LogInformation("OnTransactionDetected: {TxId}", request.TxId);
        if (OnTransactionDetected is null) return;
        await OnTransactionDetected.Invoke(this, request);
    }

    public async Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {Block}", block);
        if (OnNewBlock is null) return;
        await OnNewBlock.Invoke(this, block);
    }

    public Task MasterUpdated(long? deviceIdentifier)
    {
        _logger.LogInformation("MasterUpdated: {DeviceIdentifier}", deviceIdentifier);
        OnMasterUpdated?.Invoke(this, deviceIdentifier);
        return Task.CompletedTask;
    }
}
