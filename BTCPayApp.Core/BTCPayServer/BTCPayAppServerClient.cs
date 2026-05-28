using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Services;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;

namespace BTCPayApp.Core.BTCPayServer;


public class BTCPayAppServerClient(
    ILogger<BTCPayAppServerClient> _logger,
    ArkSignerService _arkSignerService)
    : IBTCPayAppHubClient
{
    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    public event AsyncEventHandler<string>? OnServerNodeInfo;
    public event AsyncEventHandler<ServerEvent>? OnNotifyServerEvent;
    public event AsyncEventHandler<long?>? OnMasterUpdated;

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

    public async Task MasterUpdated(long? deviceIdentifier)
    {
        _logger.LogInformation("MasterUpdated: {DeviceIdentifier}", deviceIdentifier);
        if (OnMasterUpdated is null) return;
        await OnMasterUpdated.Invoke(this, deviceIdentifier);
    }

    public Task<ECPubKey> GetPubKey(string walletId, OutputDescriptor descriptor)
        => _arkSignerService.GetPubKeyAsync(walletId, descriptor);

    public Task<MusigPartialSignature> SignMusig(string walletId, OutputDescriptor descriptor, MusigContext context, MusigPrivNonce nonce)
        => _arkSignerService.SignMusigAsync(walletId, descriptor, context, nonce);

    public Task<(ECXOnlyPubKey, SecpSchnorrSignature)> Sign(string walletId, OutputDescriptor descriptor, uint256 hash)
        => _arkSignerService.SignAsync(walletId, descriptor, hash);

    public Task<MusigPrivNonce> GenerateNonces(string walletId, OutputDescriptor descriptor, MusigContext context)
        => _arkSignerService.GenerateNoncesAsync(walletId, descriptor, context);
}
