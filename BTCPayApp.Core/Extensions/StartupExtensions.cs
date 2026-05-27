using BTCPayApp.Core.Auth;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NArk.Abstractions.Assets;
using NArk.Abstractions.Blockchain;
using NArk.Abstractions.Intents;
using NArk.Abstractions.Safety;
using NArk.Abstractions.Wallets;
using NArk.Blockchain;
using NArk.Core.Services;
using NArk.Core.Wallet;
using NArk.Hosting;
using NArk.Safety.AsyncKeyedLock;
using NArk.Storage.EfCore.Hosting;

namespace BTCPayApp.Core.Extensions;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            var dir = provider.GetRequiredService<IDataDirectoryProvider>().GetAppDataDirectory().ConfigureAwait(false).GetAwaiter().GetResult();
            options.UseSqlite($"Data Source={dir}/app.db");
        });

        // Configure logging
        LoggingConfig.ConfigureLogging(serviceCollection);

        serviceCollection.AddHostedService<AppDatabaseMigrator>();
        serviceCollection.AddSingleton<ConfigProvider, DatabaseConfigProvider>();
        serviceCollection.AddArkEfCoreStorage<AppDbContext>(o => o.StoreDateTimeOffsetAsTicks = true);
        serviceCollection.ConfigureArkade();
        serviceCollection.AddMemoryCache();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayConnectionManager>();
        serviceCollection.AddSingleton<LoggingService>();
        serviceCollection.AddSingleton<BTCPayAppServerClient>();
        serviceCollection.AddSingleton<IBTCPayAppHubClient>(provider => provider.GetRequiredService<BTCPayAppServerClient>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BTCPayConnectionManager>());
        serviceCollection.AddSingleton<AuthStateProvider>();
        serviceCollection.AddSingleton<AuthenticationStateProvider, AuthStateProvider>(provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton(sp => (IAccountManager)sp.GetRequiredService<AuthenticationStateProvider>());
        serviceCollection.AddSingleton<IAuthorizationHandler, AuthorizationHandler>();
        serviceCollection.AddAuthorizationCore(options => options.AddPolicies());

        return serviceCollection;
    }

    /// <summary>
    /// Wires the Arkade SDK (NArk) into the app: network config + transport, the
    /// on-device owner wallet bootstrap, and the SDK core services. EF Core
    /// storage (<c>AddArkEfCoreStorage</c>) is registered separately by the
    /// caller and must already be present.
    /// </summary>
    private static IServiceCollection ConfigureArkade(this IServiceCollection serviceCollection)
    {
        var networkConfig = ArkConfiguration.Resolve();

        // Network config + transport. REST/SSE matches the Arkade sample wallet
        // and works in HTTP-only environments; AddArkRestTransport also registers
        // the ArkNetworkConfig for injection.
        serviceCollection.AddArkRestTransport(networkConfig);

        // SDK infrastructure the core/background services resolve.
        serviceCollection.AddSingleton<IIntentScheduler, SimpleIntentScheduler>();
        serviceCollection.AddSingleton<ISafetyService, AsyncSafetyService>();
        serviceCollection.AddSingleton<IBitcoinBlockchain>(_ =>
            new EsploraBlockchain(new Uri(networkConfig.ExplorerUri!.TrimEnd('/') + "/api/")));
        serviceCollection.AddSingleton<IWalletProvider, DefaultWalletProvider>();
        serviceCollection.AddSingleton<IAssetManager, AssetManager>();

        // Owner-wallet bootstrap MUST run before the NArk hosted lifecycle so the
        // seed exists by the time the background services start. Hosted services
        // start in registration order, so register it before AddArkCoreServices
        // (which registers ArkHostedLifecycle).
        serviceCollection.AddSingleton<ArkWalletBootstrapService>();
        serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ArkWalletBootstrapService>());

        // SDK core services. Registers ArkHostedLifecycle as an IHostedService,
        // which starts the Sweeper/Batch/Intent/VTXO-sync background services.
        serviceCollection.AddArkCoreServices();

        // Boarding (on-chain entry) sync. AddArkCoreServices/ArkHostedLifecycle do
        // NOT start the boarding poller, so wire it here (matching the NArk README):
        // BoardingUtxoSyncService queries the IBitcoinBlockchain (Esplora, above)
        // for confirmed UTXOs at our boarding addresses and upserts them into VTXO
        // storage; BoardingUtxoPollService is the IHostedService that runs it every
        // 30s while unspent boarding VTXOs exist, so deposits get swept into the
        // Arkade without manual intervention.
        serviceCollection.AddSingleton<BoardingUtxoSyncService>();
        serviceCollection.AddSingleton<BoardingUtxoPollService>();
        serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BoardingUtxoPollService>());

        // On-demand boarding-address derivation for the owner wallet.
        serviceCollection.AddSingleton<ArkadeWalletService>();

        return serviceCollection;
    }
}
