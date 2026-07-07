using BTCPayApp.Core.BTCPayServer;
using BTCPayServer.Lightning;

namespace BTCPayApp.UI.Models;

public enum TransactionPaymentMethod
{
    Onchain,
    Lightning
}

public enum TransactionType
{
    Send,
    Receive
}

public class TransactionModel
{
    public string? Id { get; set; }
    public required LightMoney Value { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public TransactionType Type { get; set; }
    public TransactionPaymentMethod PaymentMethod { get; set; }
    public TxResp? OnchainTransaction { get; set; }
}
