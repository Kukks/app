using BTCPayApp.Core.BTCPayServer;
using BTCPayServer.Client.Models;
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

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Connected, node.ConnectionManager.ConnectionState), 30_000);

        // Store management works through the connected device.
        Assert.Null(node.AccountManager.CurrentStore);
        var store = await node.AccountManager.GetClient().CreateStore(new CreateStoreRequest { Name = "Store1" });
        Assert.True(await node.AccountManager.CheckAuthenticated(true));
        Assert.True((await node.AccountManager.SetCurrentStoreId(store.Id)).Succeeded);
        Assert.Equal(store.Id, node.AccountManager.CurrentStore?.Id);
    }
}
