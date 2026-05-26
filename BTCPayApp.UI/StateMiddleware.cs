using BTCPayApp.Core.Auth;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using BTCPayApp.UI.Features;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

namespace BTCPayApp.UI;

public class StateMiddleware(
    ConfigProvider configProvider,
    BTCPayConnectionManager btcPayConnectionManager,
    BTCPayAppServerClient btcpayAppServerClient,
    IAccountManager accountManager,
    NavigationManager navigationManager,
    ILogger<StateMiddleware> logger,
    IDispatcher _dispatcher)
    : Middleware
{

    public const string UiStateConfigKey = "uistate";
    private CancellationTokenSource? _ratesCts;
    private bool _previouslyConnected;

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (_, _) =>
            {
                var state = (UIState)uiStateFeature.GetState() with { Instance = null };
                await configProvider.Set(UiStateConfigKey, state, false);
            };

            _ = store.Initialized.ContinueWith(_ => ListenIn(dispatcher));
        }

        await base.InitializeAsync(dispatcher, store);
    }

    private async Task RefreshRates(IDispatcher dispatcher, CancellationToken token)
    {
        while (token.IsCancellationRequested is false)
        {
            var storeInfo = accountManager.CurrentStore;
            if (storeInfo != null) dispatcher.Dispatch(new StoreState.FetchRates(storeInfo));
            await Task.Delay(TimeSpan.FromMinutes(5), token);
        }
    }

    private Task ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new UserState.SetInfo(accountManager.UserInfo, null));

        btcPayConnectionManager.ConnectionChanged += (_, _) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));

            // refresh after returning from the background
            if (btcPayConnectionManager.ConnectionState == BTCPayConnectionState.ConnectedFinishedInitialSync && !_previouslyConnected)
            {
                _previouslyConnected = true;
            }
            else if (btcPayConnectionManager.ConnectionState == BTCPayConnectionState.Syncing && _previouslyConnected && accountManager.CurrentStore is { } store)
            {
                dispatcher.Dispatch(new StoreState.RefreshStore(store));
            }
            return Task.CompletedTask;
        };

        accountManager.OnStoreChanged += (_, storeInfo) =>
        {
            dispatcher.Dispatch(new StoreState.SetStoreInfo(storeInfo));
            if (storeInfo != null)
            {
                dispatcher.Dispatch(new StoreState.FetchBalances(storeInfo.Id));
                if (storeInfo.PosAppId != null)
                    dispatcher.Dispatch(new StoreState.FetchPointOfSaleStats(storeInfo.PosAppId));
            }

            navigationManager.NavigateTo(Routes.Index);
            return Task.CompletedTask;
        };

        accountManager.OnUserInfoChanged += (_, userInfo) =>
        {
            dispatcher.Dispatch(new UserState.SetInfo(userInfo, null));
            return Task.CompletedTask;
        };

        btcpayAppServerClient.OnNotifyServerEvent += async (_, serverEvent) =>
        {
            logger.LogDebug("Received Server Event: {Type} - {Info} ({Detail})", serverEvent.Type, serverEvent.ToString(), serverEvent.Detail ?? "no details");
            var currentUserId = accountManager.UserInfo?.UserId;
            if (string.IsNullOrEmpty(currentUserId)) return;
            var currentStore = accountManager.CurrentStore;
            var isCurrentUser = serverEvent.UserId == currentUserId;
            var isCurrentStore = serverEvent.StoreId != null && currentStore != null && serverEvent.StoreId == currentStore.Id;
            switch (serverEvent.Type)
            {
                case "app-created":
                    if (isCurrentStore && currentStore!.PosAppId == null)
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(currentStore.PosAppId!));
                    break;
                case "app-deleted":
                    if (isCurrentStore && currentStore!.PosAppId == serverEvent.AppId)
                    {
                        var store = await accountManager.EnsureStorePos(currentStore, true);
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(store.PosAppId!));
                    }
                    break;
                case "app-updated":
                    if (isCurrentStore && currentStore!.PosAppId == serverEvent.AppId)
                        dispatcher.Dispatch(new StoreState.FetchPointOfSale(currentStore.PosAppId!));
                    break;
                case "user-updated":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.CheckAuthenticated(true);
                    break;
                case "user-deleted":
                    if (currentUserId == serverEvent.UserId)
                        await accountManager.Logout();
                    break;
                case "notifications-updated":
                    if (currentStore != null)
                        dispatcher.Dispatch(new StoreState.FetchNotifications(currentStore.Id));
                    break;
                case "invoice-updated":
                    if (isCurrentStore)
                    {
                        dispatcher.Dispatch(new StoreState.FetchInvoices(serverEvent.StoreId!));
                        if (serverEvent.Detail is "Processing" or "Settled")
                        {
                            dispatcher.Dispatch(new StoreState.FetchBalances(serverEvent.StoreId!));
                            if (currentStore!.PosAppId != null)
                                dispatcher.Dispatch(new StoreState.FetchPointOfSaleStats(currentStore.PosAppId));
                        }
                    }
                    break;
                case "store-created":
                case "store-updated":
                case "store-removed":
                case "store-user-added":
                case "store-user-updated":
                case "store-user-removed":
                    if (serverEvent.StoreId != null)
                    {
                        await accountManager.CheckAuthenticated(true);
                        if (currentStore == null || !isCurrentStore) return;
                        if (serverEvent.Type is "store-user-removed" && isCurrentUser)
                            await accountManager.SetCurrentStoreId(null);
                        if (serverEvent.Type is "store-removed")
                            await accountManager.SetCurrentStoreId(null);
                        if (serverEvent.Type is "store-updated")
                            dispatcher.Dispatch(new StoreState.FetchStore(serverEvent.StoreId!));
                    }
                    break;
            }
        };

        _ratesCts = new CancellationTokenSource();
        _ = RefreshRates(dispatcher, _ratesCts.Token);

        return Task.CompletedTask;
    }
}
