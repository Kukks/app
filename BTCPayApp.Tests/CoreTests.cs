using BTCPayApp.Core.Backup;
using BTCPayApp.Core.BTCPayServer;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public class CoreTests(ITestOutputHelper output)
{
    private string GetEnvironment(string variable, string defaultValue)
    {
        var var = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(var) ? defaultValue : var;
    }

    [Fact]
    public async Task CanStartAppCore()
    {
        var btcpayUri = new Uri(GetEnvironment("BTCPAY_SERVER_URL", "https://localhost:14142"));
        using var node = await HeadlessTestNode.Create("Node1", output);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.WaitingForAuth, node.ConnectionManager.ConnectionState));

        var username = Guid.NewGuid() + "@gg.com";

        Assert.True((await node.AccountManager.Register(btcpayUri.AbsoluteUri, username, username)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        await node.AccountManager.Logout();
        Assert.False(await node.AuthStateProvider.CheckAuthenticated());
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.Account?.OwnerToken);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node.ConnectionManager.ConnectionState), 30_000);

        // Establish the encryption key that backs the cross-device sync/backup seam.
        var encryptionMnemonic = new Mnemonic(Wordlist.English);
        Assert.True(await node.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(encryptionMnemonic.ToString()));

        await node.AccountManager.Logout();
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.Account?.OwnerToken);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node.ConnectionManager.ConnectionState), 30_000);

        // A second device logging into the same account must import the encryption key before it can sync.
        using var node2 = await HeadlessTestNode.Create("Node2", output);
        Assert.True((await node2.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node2.AuthStateProvider.CheckAuthenticated());

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.WaitingForEncryptionKey, node2.ConnectionManager.ConnectionState));
        Assert.False(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(new Mnemonic(Wordlist.English).ToString()));
        Assert.True(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(encryptionMnemonic.ToString()));

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Syncing, node2.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsSecondary, node2.ConnectionManager.ConnectionState));

        // Hand the primary role over to the second device.
        await node.ConnectionManager.SwitchToSecondary();
        output.WriteLine("SLAVE CHECKPOINT");

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsSecondary, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node2.ConnectionManager.ConnectionState));

        // Store management still works through the connected device.
        Assert.Null(node2.AccountManager.CurrentStore);
        var store = await node2.AccountManager.GetClient().CreateStore(new CreateStoreRequest { Name = "Store1" });
        Assert.True(await node2.AccountManager.CheckAuthenticated(true));
        Assert.True((await node2.AccountManager.SetCurrentStoreId(store.Id)).Succeeded);
        Assert.Equal(store.Id, node2.AccountManager.CurrentStore?.Id);
    }
}
