using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NArk.Abstractions.Wallets;
using NArk.Core.Transport;
using NArk.Core.Wallet;
using NBitcoin;

namespace BTCPayApp.Core.Services;

/// <summary>
/// Stands up the on-device owner wallet for the Arkade SDK. The owner wallet
/// holds the HD seed and is the signer. This service:
/// <list type="number">
/// <item>generates a BIP-39 mnemonic on first run and persists it locally via
/// <see cref="ConfigProvider"/> (the seed never leaves the device);</item>
/// <item>registers the wallet with NArk's <see cref="IWalletStorage"/> so the
/// SDK can derive addresses and sign — building the <see cref="ArkWalletInfo"/>
/// exactly as <see cref="WalletFactory"/> does;</item>
/// <item>tracks the resulting wallet id locally so other code can resolve it.</item>
/// </list>
/// It is idempotent: on subsequent starts the existing mnemonic is reused and
/// the wallet is only re-registered if NArk storage has lost it. Registering the
/// wallet needs the Arkade server's network (to pick the BIP-86 coin type), so
/// that step retries in the background — startup never blocks on or fails
/// because of an unreachable server. Must be registered BEFORE the NArk hosted
/// lifecycle so the seed exists by the time the background services run.
/// </summary>
public class ArkWalletBootstrapService(
    ConfigProvider configProvider,
    IWalletStorage walletStorage,
    IClientTransport clientTransport,
    ILogger<ArkWalletBootstrapService> logger)
    : BaseHostedService(logger)
{
    /// <summary>Settings key holding the on-device owner-wallet BIP-39 mnemonic.</summary>
    public const string MnemonicKey = "ark:owner:mnemonic";

    /// <summary>Settings key holding the NArk wallet id of the owner wallet.</summary>
    public const string WalletIdKey = "ark:owner:walletid";

    private Task? _registrationTask;

    protected override async Task ExecuteStartAsync(CancellationToken cancellationToken)
    {
        // Local-only seed: generate once, then reuse on every subsequent start.
        var mnemonic = await configProvider.Get<string>(MnemonicKey);
        if (string.IsNullOrEmpty(mnemonic))
        {
            mnemonic = new Mnemonic(Wordlist.English).ToString();
            await configProvider.Set(MnemonicKey, mnemonic, backup: false);
            logger.LogInformation("Generated on-device Arkade owner wallet seed");
        }

        // Registering with NArk storage needs the server's network to build the
        // account descriptor. Do it off the startup path so an unreachable
        // Arkade server can't block or crash the host.
        var seed = mnemonic;
        _registrationTask = Task.Run(() => EnsureWalletRegisteredAsync(seed, cancellationToken), cancellationToken);
    }

    private async Task EnsureWalletRegisteredAsync(string mnemonic, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // If the wallet id is already tracked and present in NArk storage we're done.
                var walletId = await configProvider.Get<string>(WalletIdKey);
                if (!string.IsNullOrEmpty(walletId) &&
                    await walletStorage.GetWalletById(walletId, cancellationToken) is not null)
                {
                    return;
                }

                // Build the ArkWalletInfo exactly as the SDK does (HD, BIP-86
                // wildcard descriptor) and persist it. The coin type comes from
                // the server's network, so this is the only step that needs the
                // network — hence the retry loop around it.
                var serverInfo = await clientTransport.GetServerInfoAsync(cancellationToken);
                var wallet = await WalletFactory.CreateWallet(mnemonic, null, serverInfo, cancellationToken);
                await walletStorage.UpsertWallet(wallet, updateIfExists: false, cancellationToken);
                await configProvider.Set(WalletIdKey, wallet.Id, backup: false);
                logger.LogInformation("Registered Arkade owner wallet {WalletId}", wallet.Id);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Arkade owner wallet registration deferred (server not reachable yet); retrying");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    protected override async Task ExecuteStopAsync(CancellationToken cancellationToken)
    {
        if (_registrationTask is not null)
        {
            try
            {
                await _registrationTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
