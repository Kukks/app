using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using NBitcoin;
using NBitcoin.Scripting;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;

namespace BTCPayApp.Core.BTCPayServer;

//methods available on the hub in the client
public interface IBTCPayAppHubClient
{
    Task NotifyServerEvent(ServerEvent ev);
    Task NotifyNetwork(string network);
    Task NotifyServerNode(string nodeInfo);
    Task TransactionDetected(TransactionDetectedRequest request);
    Task NewBlock(string block);
    // Notifies the device which BTCPayApp instance is currently master for its
    // user (or null if no instance is). Called from BTCPayAppState when the
    // master flag is updated for any connection in the user's group.
    Task MasterUpdated(long? deviceIdentifier);

    // Remote-signer callbacks. Mirror NArk.Abstractions.Wallets.IRemoteSignerTransport
    // (NArk master, post-#107/#113/#114) with a walletId first arg so the
    // server-side BTCPayAppDeviceProxy can address the right owner-wallet on
    // the connected device. The device-side BTCPayAppServerClient forwards each
    // call to ArkSignerService, which resolves the local IArkadeWalletSigner
    // and validates the walletId matches the device's owner wallet before
    // signing. The MuSig2 secret nonce never crosses this wire: GenerateNonces
    // returns only the public half, and SignMusig refers to the secret half
    // by the same sessionId the local signer indexed it under.
    Task<bool> KnowsWallet(string walletId);
    Task<ECPubKey> GetPubKey(string walletId, OutputDescriptor descriptor);
    Task<MusigPartialSignature> SignMusig(string walletId, OutputDescriptor descriptor, MusigContext context, string sessionId);
    Task<(ECXOnlyPubKey, SecpSchnorrSignature)> Sign(string walletId, OutputDescriptor descriptor, uint256 hash);
    Task<MusigPubNonce> GenerateNonces(string walletId, OutputDescriptor descriptor, MusigContext context, string sessionId);
}

//methods available on the hub in the server
public interface IBTCPayAppHubServer
{
    Task<Dictionary<string,string>> Pair(PairRequest request);
    Task<AppHandshakeResponse> Handshake(AppHandshake request);
    Task<bool> BroadcastTransaction(string tx);
    Task<decimal> GetFeeRate(int blockTarget);
    Task<BestBlockResponse?> GetBestBlock();
    Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string identifier, string[] txIds, string[] outpoints);
    Task<ScriptResponse> DeriveScript(string identifier);
    Task TrackScripts(string identifier, string[] scripts);
    Task<string> UpdatePsbt(string[] identifiers, string psbt);
    Task<Dictionary<string, CoinResponse[]>> GetUTXOs(string[] identifiers);
    Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers);
}

public class ServerEvent
{
    public string Type { get; set; } = null!;
    public string? StoreId { get; set; }
    public string? UserId { get; set; }
    public string? AppId { get; set; }
    public string? InvoiceId { get; set; }
    public string? Detail { get; set; }
}

public record TxResp
{
    public string TransactionId { get; set; } = null!;
    public long Confirmations { get; set; }
    public long? Height { get; set; }
    public decimal BalanceChange { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public override string ToString()
    {
        return $"{{ Confirmations = {Confirmations}, Height = {Height}, BalanceChange = {BalanceChange}, Timestamp = {Timestamp}, TransactionId = {TransactionId} }}";
    }
}

public class TransactionDetectedRequest
{
    public string? Identifier { get; set; }
    public string? TxId { get; set; }
    public string[]? SpentScripts { get; set; }
    public string[]? ReceivedScripts { get; set; }
    public bool Confirmed { get; set; }
}

public class CoinResponse
{
    public bool Confirmed { get; set; }
    public string? Script { get; set; }
    public string? Outpoint { get; set; }
    public decimal Value { get; set; }
    public string? Path { get; set; }
}

public class TxInfoResponse
{
    public Dictionary<string,TransactionResponse>? Txs { get; set; }
    public Dictionary<string,string>? BlockHeaders { get; set; }
    public Dictionary<string,int>? BlockHeights { get; set; }
}

public class TransactionResponse
{
    public string? BlockHash { get; set; }
    public string? Transaction { get; set; }
}

public class BestBlockResponse
{
    public string? BlockHash { get; set; }
    public int BlockHeight { get; set; }
    public string? BlockHeader { get; set; }
}

public class ScriptResponse
{
    public string Script { get; set; } = null!;
    public string KeyPath { get; set; } = null!;
}

public class AppHandshake
{
    public string[]? Identifiers { get; set; }
}

//response about identifiers being tracked successfully
public class AppHandshakeResponse
{
    public string[]? IdentifiersAcknowledged { get; set; }
}

public class PairRequest
{
    public Dictionary<string, DerivationItem> Derivations { get; set; } = new();
}

public class DerivationItem
{
    public string? Descriptor { get; set; }
    public int Index { get; set; }
    public OutPoint[] KnownCoins { get; set; } = [];
}
