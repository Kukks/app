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
    ArkSignerService _arkSignerService,
    NArk.Core.Transport.IClientTransport _clientTransport)
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

    public Task<bool> KnowsWallet(string walletId)
        => _arkSignerService.KnowsWalletAsync(walletId);

    public async Task<string> GetPubKey(string walletId, string descriptor)
        => Convert.ToHexString(
            (await _arkSignerService.GetPubKeyAsync(walletId, await ParseDescriptor(descriptor))).ToBytes());

    public async Task<string> SignMusig(string walletId, string descriptor, string musigContext, string sessionId)
        => Convert.ToHexString(
            (await _arkSignerService.SignMusigAsync(
                walletId, await ParseDescriptor(descriptor), MusigContextWire.Deserialize(musigContext), sessionId)).ToBytes());

    public async Task<SignResponse> Sign(string walletId, string descriptor, string hash)
    {
        var (xOnlyPubKey, signature) = await _arkSignerService.SignAsync(
            walletId, await ParseDescriptor(descriptor), uint256.Parse(hash));
        return new SignResponse
        {
            XOnlyPubKey = Convert.ToHexString(xOnlyPubKey.ToBytes()),
            Signature = Convert.ToHexString(signature.ToBytes())
        };
    }

    public async Task<string> GenerateNonces(string walletId, string descriptor, string musigContext, string sessionId)
        => Convert.ToHexString(
            (await _arkSignerService.GenerateNoncesAsync(
                walletId, await ParseDescriptor(descriptor), MusigContextWire.Deserialize(musigContext), sessionId)).ToBytes());

    // The wire carries descriptors as strings; parsing needs the operator's
    // network, which the cached transport already knows.
    private async Task<OutputDescriptor> ParseDescriptor(string descriptor)
    {
        var terms = await _clientTransport.GetServerInfoAsync(CancellationToken.None);
        return OutputDescriptor.Parse(descriptor, terms.Network);
    }
}
