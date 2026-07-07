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
using NArk.Abstractions.Safety;
using NArk.Abstractions.Wallets;
using NArk.Core.Transport;
using NArk.Core.Wallet;
using NArk.Hosting;
using NArk.Safety.AsyncKeyedLock;
using NArk.Storage.EfCore.Hosting;
using NArk.Transport.RestClient;

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
    /// Wires NArk into the app for its <em>signer-only</em> role. The device
    /// holds the HD seed and answers signing requests forwarded from the paired
    /// BTCPay store via <see cref="ArkSignerService"/>; the watch-only btcpay-arkade
    /// plugin on the server is the sole owner of wallet state (VTXO sync, batch
    /// participation, intent generation, sweeping, boarding-UTXO polling).
    /// <para>
    /// We deliberately do NOT call <c>AddArkCoreServices()</c> or register the
    /// boarding-sync poller / <see cref="NArk.Abstractions.Blockchain.IBitcoinBlockchain"/>
    /// / <see cref="NArk.Abstractions.Intents.IIntentScheduler"/> / asset manager
    /// here. Running those on the device duplicates work the server already does
    /// for the same wallet and creates real coordination bugs: two sweepers
    /// racing on a maturing VTXO, two batch managers calling
    /// <c>GenerateNonces</c> against the same operator session, and (worst case)
    /// MuSig2 nonce collisions across the device-local and server-mediated
    /// signing flows that could leak the private key.
    /// </para>
    /// <para>
    /// What the device DOES need from NArk: just enough to resolve the local
    /// signer for an incoming hub-forwarded request — <see cref="IWalletProvider"/>
    /// (composed automatically from <c>Bip39KeyProvider</c> per NArk #114),
    /// <see cref="ISafetyService"/>, <see cref="IClientTransport"/> (for the
    /// one-shot <c>GetServerInfoAsync</c> the bootstrap uses to derive the
    /// BIP-86 coin type), and the EF Core storage that backs
    /// <c>IWalletStorage</c>/<c>IContractStorage</c>. Everything else is
    /// server-side.
    /// </para>
    /// </summary>
    private static IServiceCollection ConfigureArkade(this IServiceCollection serviceCollection)
    {
        // Merchant-selected network/operator, synced from the paired BTCPay store
        // by ArkadeConfigSyncService below.
        serviceCollection.AddSingleton<ArkadeOperatorConfig>();

        // Lazy network resolve via factory. The first consumer of this singleton
        // is typically the transport during hosted-service start, after the
        // migrator. But early DI traversal — e.g. a test or UI code path
        // resolving BTCPayConnectionManager before hosted services start — can
        // fire this factory before the Settings table exists. In that window
        // "no setting persisted yet" and "schema not ready yet" are semantically
        // the same: fall back to the bundled default. Once the host starts,
        // every subsequent resolve reads the merchant's actual choice.
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

        // Inline of AddArkRestTransport(config), but resolving the config via the
        // factory above instead of taking a concrete instance. The transport is
        // here because ArkWalletBootstrapService.GetServerInfoAsync needs it to
        // learn the network's coin type for the BIP-86 descriptor — it is NOT
        // wired up for streaming operator events (no AddArkCoreServices below).
        serviceCollection.AddSingleton(sp =>
            new RestClientTransport(sp.GetRequiredService<ArkNetworkConfig>().ArkUri));
        serviceCollection.AddSingleton<IClientTransport>(sp =>
        {
            var inner = sp.GetRequiredService<RestClientTransport>();
            var logger = sp.GetService<ILogger<CachingClientTransport>>();
            return new CachingClientTransport(inner, logger);
        });

        // The minimum NArk surface needed to resolve a local IArkadeWalletSigner
        // for an incoming hub-forwarded signing request. DefaultWalletProvider's
        // ctor takes (IClientTransport, ISafetyService, IWalletStorage,
        // IContractStorage); the storages come from AddArkEfCoreStorage which
        // ConfigureBTCPayAppCore registered above.
        serviceCollection.AddSingleton<ISafetyService, AsyncSafetyService>();
        serviceCollection.AddSingleton<IWalletProvider, DefaultWalletProvider>();

        // Owner-wallet bootstrap: generates the on-device mnemonic on first run
        // and registers an HD ArkWalletInfo with NArk storage so DefaultWalletProvider
        // can build a Bip39KeyProvider for it. Idempotent against the operator —
        // the server-side btcpay-arkade plugin separately registers its watch-only
        // mirror for the same descriptor (→ same walletId).
        serviceCollection.AddSingleton<ArkWalletBootstrapService>();
        serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ArkWalletBootstrapService>());

        // Pulls the paired BTCPay store's Arkade network config off the SignalR
        // hub on every connect and persists it locally. The device does not
        // pick a network — the plugin dictates it via GetArkadeConfig().
        serviceCollection.AddSingleton<ArkadeConfigSyncService>();
        serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ArkadeConfigSyncService>());

        // The signer + status surfaces. ArkSignerService bridges remote-signing
        // requests that the BTCPayServer companion plugin forwards over the
        // SignalR hub down to the local IArkadeWalletSigner. The on-device seed
        // never leaves this process — every call validates the requested
        // walletId matches the owner wallet before signing.
        serviceCollection.AddSingleton<ArkSignerService>();
        serviceCollection.AddSingleton<MnemonicBackupService>();
        serviceCollection.AddSingleton<SignerStatusService>();
        serviceCollection.AddSingleton<MainnetPreflightService>();

        return serviceCollection;
    }
}
