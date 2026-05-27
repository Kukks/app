using BTCPayApp.Core.Contracts;
using NArk.Abstractions.Contracts;
using NArk.Abstractions.Wallets;
using NArk.Core.Contracts;
using NArk.Core.Services;
using NArk.Core.Transport;
using NBitcoin;

namespace BTCPayApp.Core.Services;

/// <summary>
/// On-demand Arkade wallet operations for the on-device owner wallet that
/// <see cref="ArkWalletBootstrapService"/> registers. Today this exposes the
/// boarding (on-chain entry) flow: deriving the P2TR address a user funds with
/// BTC, which NArk's boarding sync/batch pipeline later sweeps into the Arkade.
/// </summary>
public class ArkadeWalletService(
    ConfigProvider configProvider,
    IWalletProvider walletProvider,
    IContractService contractService,
    IClientTransport clientTransport)
{
    /// <summary>
    /// Derives the next boarding (on-chain entry) address for the owner wallet.
    /// The returned P2TR address is funded with BTC by the user; once the deposit
    /// confirms, NArk's boarding UTXO sync picks it up and the batch pipeline
    /// settles it into the Arkade VTXO tree.
    /// <para>
    /// Derivation goes through <see cref="IContractService.DeriveContract"/>, which
    /// persists the boarding contract (so the sync/poll services can watch its
    /// address) — the contract is therefore persisted exactly once here.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The on-chain P2TR boarding address (tb1p.../bc1p.../bcrt1p...).</returns>
    /// <exception cref="InvalidOperationException">
    /// The owner wallet has not finished registering with NArk yet (e.g. the
    /// Arkade server was unreachable and <see cref="ArkWalletBootstrapService"/>
    /// is still retrying). The caller should surface this and retry rather than
    /// treat any address as valid.
    /// </exception>
    public async Task<BitcoinAddress> GetBoardingAddressAsync(CancellationToken cancellationToken = default)
    {
        var walletId = await configProvider.Get<string>(ArkWalletBootstrapService.WalletIdKey);
        if (string.IsNullOrEmpty(walletId))
            throw new InvalidOperationException(
                "Arkade owner wallet is not registered yet; cannot derive a boarding address. " +
                "Registration runs in the background and needs the Arkade server reachable — retry shortly.");

        // The wallet id is tracked locally but its NArk storage record may still be
        // absent (e.g. storage was reset since the id was last persisted). Resolving
        // the address provider up front lets us fail with a clear message instead of
        // a NullReferenceException deep inside contract derivation.
        var addressProvider = await walletProvider.GetAddressProviderAsync(walletId, cancellationToken);
        if (addressProvider is null)
            throw new InvalidOperationException(
                $"Arkade owner wallet '{walletId}' is not present in wallet storage; cannot derive a boarding address. " +
                "Registration runs in the background — retry shortly.");

        var contract = await contractService.DeriveContract(
            walletId,
            NextContractPurpose.Boarding,
            ContractActivityState.Active,
            cancellationToken: cancellationToken);

        var network = (await clientTransport.GetServerInfoAsync(cancellationToken)).Network;
        return ((ArkBoardingContract)contract).GetOnchainAddress(network);
    }
}
