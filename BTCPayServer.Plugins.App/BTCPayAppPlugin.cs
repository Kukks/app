using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.App.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.App;

public class BTCPayAppPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new () { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" },
        // The device-signer bridge (BTCPayAppDeviceProxy) implements IBTCPayAppDeviceProxy from the
        // Arkade plugin. Declaring the dependency makes PluginManager link this plugin's load context
        // to the Arkade plugin's, so NArk/ArkPayServer types resolve to the same Type instances —
        // without it, each plugin loads its own copy and the DI registration silently misses.
        new () { Identifier = "BTCPayServer.Plugins.ArkPayServer", Condition = ">=2.2.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddBTCPayApp();
    }

    public override void Execute(IApplicationBuilder applicationBuilder, IServiceProvider provider)
    {
        base.Execute(applicationBuilder, provider);
        applicationBuilder.UseBTCPayApp();
    }
}
