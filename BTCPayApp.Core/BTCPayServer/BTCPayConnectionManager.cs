using System.Net;
using System.Net.WebSockets;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using TypedSignalR.Client;

namespace BTCPayApp.Core.BTCPayServer;

public class BTCPayConnectionManager(
    IServiceProvider serviceProvider,
    IAccountManager accountManager,
    AuthenticationStateProvider authStateProvider,
    ILogger<BTCPayConnectionManager> logger,
    BTCPayAppServerClient btcPayAppServerClient,
    IBTCPayAppHubClient btcPayAppServerClientInterface,
    ConfigProvider configProvider)
    : BaseHostedService(logger), IHubConnectionObserver
{
    private BTCPayConnectionState _connectionState = BTCPayConnectionState.Init;
    private CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IDisposable? _subscription;
    private IBTCPayAppHubServer? _hubProxy;
    public IBTCPayAppHubServer? HubProxy
    {
        get => Connection?.State == HubConnectionState.Connected ? _hubProxy : null;
        private set => _hubProxy = value;
    }
    private HubConnection? Connection { get; set; }
    public Network? ReportedNetwork { get; private set; }
    public string? ReportedNodeInfo { get; set; }
    public bool RunningInBackground { get; set; }

    public event AsyncEventHandler<(BTCPayConnectionState Old, BTCPayConnectionState New)>? ConnectionChanged;

    public BTCPayConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            _lock.Wait();
            try
            {
                if (_connectionState == value) return;
                var old = _connectionState;
                _connectionState = value;
                logger.LogInformation("Connection state changed{BgInfo}: {Old} -> {ConnectionState}", BgInfo, old, _connectionState);
                ConnectionChanged?.Invoke(this, (old, _connectionState));
            }
            finally
            {
               _lock.Release();
            }
        }
    }

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConnectionChanged += OnConnectionChanged;
        authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        btcPayAppServerClient.OnNotifyNetwork += OnNotifyNetwork;
        btcPayAppServerClient.OnNotifyServerEvent += OnNotifyServerEvent;
        btcPayAppServerClient.OnServerNodeInfo += OnServerNodeInfo;
        await OnConnectionChanged(this, (BTCPayConnectionState.Init, BTCPayConnectionState.Init));
    }

    private async Task OnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        var newState = e.New;
        try
        {
            var account = accountManager.Account;
            switch (e.New)
            {
                case BTCPayConnectionState.Init:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
                case BTCPayConnectionState.WaitingForAuth:
                    if (account is not null && await accountManager.CheckAuthenticated())
                    {
                        newState = BTCPayConnectionState.Connecting;
                    }
                    break;
                case BTCPayConnectionState.Connecting:
                    if (account is null)
                    {
                        newState = BTCPayConnectionState.WaitingForAuth;
                        break;
                    }
                    await Kill();
                    var url = new Uri(new Uri(account.BaseUri), "hub/btcpayapp").ToString();
                    var connection = new HubConnectionBuilder()
                        .AddNewtonsoftJsonProtocol(options =>
                        {
                            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(options.PayloadSerializerSettings);
                            options.PayloadSerializerSettings.Converters.Add(new global::BTCPayServer.Lightning.JsonConverters.LightMoneyJsonConverter());
                        })
                        .WithUrl(url, options =>
                        {
                            options.AccessTokenProvider = () =>
                                Task.FromResult(accountManager.Account?.OwnerToken);
                            options.HttpMessageHandlerFactory = serviceProvider
                                .GetService<Func<HttpMessageHandler, HttpMessageHandler>>();
                            options.WebSocketConfiguration =
                                serviceProvider.GetService<Action<ClientWebSocketOptions>>();
                        })
                        .Build();

                    _subscription = connection.Register(btcPayAppServerClientInterface);
                    HubProxy = new ExceptionWrappedHubProxy(connection, logger);

                    if (connection.State == HubConnectionState.Disconnected)
                    {
                        try
                        {
                            connection.Closed += OnClosed;
                            connection.Reconnected += OnReconnected;
                            connection.Reconnecting += OnReconnecting;
                            await connection.StartAsync();
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
                        {
                            await accountManager.Logout();
                            logger.LogInformation("Signed out user because of unauthorized response{BgInfo}", BgInfo);
                        }
                        catch (Exception ex)
                        {
                            await Task.Delay(500);
                            if (ex is not TaskCanceledException)
                                logger.LogError("Error while connecting to hub{BgInfo}: {Message}", BgInfo, ex.Message);
                        }
                    }
                    Connection = connection;
                    newState = Connection.State switch
                    {
                        HubConnectionState.Connected => BTCPayConnectionState.Connected,
                        HubConnectionState.Connecting => BTCPayConnectionState.Connecting,
                        _ => BTCPayConnectionState.WaitingForAuth
                    };
                    break;
                case BTCPayConnectionState.Connected:
                    var config = await configProvider.Get<BTCPayAppConfig>(BTCPayAppConfig.Key);
                    if (!string.IsNullOrEmpty(config?.CurrentStoreId))
                    {
                        await accountManager.SetCurrentStoreId(config.CurrentStoreId);
                    }
                    break;
                case BTCPayConnectionState.Disconnected:
                    newState = BTCPayConnectionState.WaitingForAuth;
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while changing connection state from {Old} to {New}{BgInfo}", e.Old, e.New, BgInfo);
            throw;
        }
        finally
        {
            _ = Task.Run(() => ConnectionState = newState);
        }
    }

    private Task OnServerNodeInfo(object? sender, string? e)
    {
        ReportedNodeInfo = e;
        return Task.CompletedTask;
    }

    private Task OnNotifyServerEvent(object? sender, ServerEvent e)
    {
        logger.LogInformation("OnNotifyServerEvent{BgInfo}: {Type} - {Details}", BgInfo, e.Type, e.ToString());
        return Task.CompletedTask;
    }

    private Task OnNotifyNetwork(object? sender, string e)
    {
        ReportedNetwork = Network.GetNetwork(e);
        return Task.CompletedTask;
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        await WrapInLock(async () =>
        {
            try
            {
                await task;
                var authState = await accountManager.CheckAuthenticated();
                if (ConnectionState == BTCPayConnectionState.WaitingForAuth && authState)
                {
                    ConnectionState = BTCPayConnectionState.Connecting;
                }
                else if (ConnectionState > BTCPayConnectionState.WaitingForAuth && !authState)
                {
                    ConnectionState = BTCPayConnectionState.WaitingForAuth;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while handling authentication state change{BgInfo}", BgInfo);
            }
        }, _cts.Token);
    }

    private async Task Kill()
    {
        if (Connection is not null)
        {
            logger.LogWarning("Killing connection{BgInfo}", BgInfo);
        }
        var conn = Connection;
        Connection = null;
        if (conn is not null)
        {
            conn.Closed -= OnClosed;
            conn.Reconnected -= OnReconnected;
            conn.Reconnecting -= OnReconnecting;

            await conn.StopAsync();
        }
        _subscription?.Dispose();
        _subscription = null;
        HubProxy = null;
    }

    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        await Kill();
        authStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        btcPayAppServerClient.OnNotifyNetwork -= OnNotifyNetwork;
        ConnectionChanged -= OnConnectionChanged;
    }

    public Task OnClosed(Exception? ex)
    {
        logger.LogError("Hub connection closed{BgInfo}: {Message}", BgInfo, ex?.Message);
        if (Connection?.State == HubConnectionState.Disconnected && ConnectionState != BTCPayConnectionState.Connecting)
        {
            ConnectionState = BTCPayConnectionState.Disconnected;
        }

        return Task.CompletedTask;
    }

    public Task OnReconnected(string? connectionId)
    {
        logger.LogInformation("Hub connection reconnected{BgInfo}", BgInfo);
        ConnectionState = BTCPayConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task OnReconnecting(Exception? ex)
    {
        logger.LogWarning("Hub connection reconnecting{BgInfo}: {Message}", BgInfo, ex?.Message);
        ConnectionState = BTCPayConnectionState.Connecting;
        return Task.CompletedTask;
    }

    private string BgInfo => RunningInBackground ? " (in background mode)" : string.Empty;
}
