using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Plugins.App.Data;
using BTCPayServer.Plugins.App.Services;
using BTCPayServer.Plugins.ArkPayServer.Services;
using Laraue.EfCoreTriggers.PostgreSql.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.App.Extensions;

public static class AppExtensions
{
    public static IServiceCollection AddBTCPayApp(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddGrpc();
        serviceCollection.AddSingleton<BTCPayAppState>();
        serviceCollection.AddSingleton<AppPluginDbContextFactory>();
        serviceCollection.AddDbContext<AppPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<AppPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
            o.UsePostgreSqlTriggers();
        });
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<BTCPayAppState>());
        serviceCollection.AddStartupTask<AppPluginMigrationRunner>();

        // Cross-plugin seam: register the BTCPayApp-backed implementation of
        // btcpay-arkade's IBTCPayAppDeviceProxy. ArkadePlugin's DI factory
        // falls back to a "no proxy installed" sentinel transport if we don't
        // register here — when this companion plugin is loaded, it picks up
        // BTCPayAppDeviceProxy and forwards Arkade signing for Remote-typed
        // wallets to the paired device over the SignalR hub.
        serviceCollection.AddSingleton<IBTCPayAppDeviceProxy, BTCPayAppDeviceProxy>();
        return serviceCollection;
    }

    public static void UseBTCPayApp(this IApplicationBuilder builder)
    {
        builder.UseEndpoints(routeBuilder =>
        {
            routeBuilder.MapHub<BTCPayAppHub>("hub/btcpayapp");
        });
    }
}
