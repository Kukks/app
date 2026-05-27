using System.Text.Json.Serialization;

namespace BTCPayApp.Core.BTCPayServer;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BTCPayConnectionState
{
    Init,
    WaitingForAuth,
    Connecting,
    Connected,
    Disconnected
}
