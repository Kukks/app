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
using Microsoft.Extensions.Logging;
using NArk.Abstractions.Assets;
using NArk.Abstractions.Blockchain;
using NArk.Abstractions.Intents;
using NArk.Abstractions.Safety;
using NArk.Abstractions.Wallets;
using NArk.Blockchain;
using NArk.Core.Services;
using NArk.Core.Transport;
using NArk.Transport.RestClient;
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
            // NArk's EF entity configuration evolves faster than our app-level migrations.
            // The runtime model legitimately differs from AppDbContextModelSnapshot in ways
            // that don't affect the SQL schema (annotations, HasComment, etc.); raising
            // PendingModelChangesWarning to an error blocks MigrateAsync on startup. Demote
            // it to a log warning so the migrator proceeds — the underlying schema is correct.
            options.ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
        serviceCollection.AddSingleton<ArkadeOperatorConfig>();

        // Lazy network resolve via factory. The first consumer of this singleton
        // is typically the transport (during hosted-service start, after the
        // migrator). But early DI traversal — e.g. when a test or UI code path
        // resolves BTCPayConnectionManager before the host's hosted services
        // start — can fire this factory before the Settings table exists. In
        // that window "no setting persisted yet" and "schema not ready yet" are
        // semantically the same: fall back to the bundled default. Once the
        // host starts, every subsequent resolve reads the merchant's choice
        // (the factory is a singleton, so this is a one-shot fallback).
        serviceCollection.AddSingleton<ArkNetworkConfig>(sp =>
        {
            try
            {
                return sp.GetRequiredService<ArkadeOperatorConfig>().ResolveAsync().GetAwaiter().GetResult();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                return ArkConfiguration.Resolve();
            }
        });

        // Inline what AddArkRestTransport(config) does
        // (submodules/NArk/NArk.Core/Hosting/ServiceCollectionExtensions.cs:203-220),
        // but resolving the config via the factory above instead of taking a
        // concrete instance. No NArk changes required.
        serviceCollection.AddSingleton(sp =>
            new RestClientTransport(sp.GetRequiredService<ArkNetworkConfig>().ArkUri));
        serviceCollection.AddSingleton<IClientTransport>(sp =>
        {
            var inner = sp.GetRequiredService<RestClientTransport>();
            var logger = sp.GetService<ILogger<CachingClientTransport>>();
            return new CachingClientTransport(inner, logger);
        });

        // SDK infrastructure the core/background services resolve.
        serviceCollection.AddSingleton<IIntentScheduler, SimpleIntentScheduler>();
        serviceCollection.AddSingleton<ISafetyService, AsyncSafetyService>();
        serviceCollection.AddSingleton<IBitcoinBlockchain>(sp =>
        {
            var cfg = sp.GetRequiredService<ArkNetworkConfig>();
            return new EsploraBlockchain(new Uri(cfg.ExplorerUri!.TrimEnd('/') + "/api/"));
        });
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

        // Bridges remote-signing requests that the BTCPayServer companion plugin
        // forwards over the SignalR hub down to the local IArkadeWalletSigner.
        // The on-device seed never leaves this process — every call validates
        // the requested walletId matches the owner wallet before signing.
        serviceCollection.AddSingleton<ArkSignerService>();
        serviceCollection.AddSingleton<MnemonicBackupService>();
        serviceCollection.AddSingleton<SignerStatusService>();
        serviceCollection.AddSingleton<MainnetPreflightService>();

        return serviceCollection;
    }
}
