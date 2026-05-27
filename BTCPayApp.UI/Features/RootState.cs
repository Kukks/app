using BTCPayApp.Core.BTCPayServer;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState
{
    public BTCPayConnectionState ConnectionState;

    public record ConnectionStateUpdatedAction(BTCPayConnectionState State);

    protected class ConnectionUpdatedReducer : Reducer<RootState, ConnectionStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, ConnectionStateUpdatedAction action)
        {
            return state with { ConnectionState = action.State };
        }
    }
}
