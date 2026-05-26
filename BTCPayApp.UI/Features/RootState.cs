using BTCPayApp.Core.BTCPayServer;
using Fluxor;
using Fluxor.Blazor.Web.Middlewares.Routing;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record RootState
{
    public BTCPayConnectionState ConnectionState;

    public record ConnectionStateUpdatedAction(BTCPayConnectionState State);

    public class ConnectionEffects(NavigationManager navigationManager)
    {
        [EffectMethod]
        public Task HandleConnectionStateUpdatedAction(RootState.ConnectionStateUpdatedAction action, IDispatcher dispatcher)
        {
            if (action.State == BTCPayConnectionState.WaitingForEncryptionKey)
            {
                dispatcher.Dispatch(new GoAction(navigationManager.ToAbsoluteUri(Routes.Pairing).ToString()));
            }
            return Task.CompletedTask;
        }
    }

    protected class ConnectionUpdatedReducer : Reducer<RootState, ConnectionStateUpdatedAction>
    {
        public override RootState Reduce(RootState state, ConnectionStateUpdatedAction action)
        {
            return state with { ConnectionState = action.State };
        }
    }
}
