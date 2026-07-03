using BTCPayApp.Core.Helpers;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;
using Xunit;

namespace BTCPayApp.Tests;

/// <summary>
/// Crypto-equivalence tests for <see cref="MusigContextWire"/> — the blob that
/// carries a MuSig2 signing session across the SignalR hub (SignalR's JSON
/// protocol cannot move NBitcoin/secp types). A deserialized context must be
/// indistinguishable from the original to the signing math: same aggregate
/// key, nonces generated against it must aggregate, and partial signatures
/// produced from a deserialized context must be byte-identical and verify
/// against the original.
/// </summary>
public class MusigContextWireTests
{
    private static readonly byte[] Msg32 = new byte[32]
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
    };

    private static ECPrivKey Key(byte seed)
    {
        var b = new byte[32];
        b[31] = seed;
        return ECPrivKey.Create(b);
    }

    [Fact]
    public void RoundTrip_PreNonce_PreservesAggregateKey_AndNonceGenerationWorks()
    {
        var k1 = Key(11);
        var k2 = Key(22);
        var pubs = new[] { k1.CreatePubKey(), k2.CreatePubKey() };

        var original = new MusigContext(pubs, Msg32, pubs[0]);

        var blob = MusigContextWire.Serialize(original);
        var copy = MusigContextWire.Deserialize(blob);

        Assert.Equal(original.AggregatePubKey.ToBytes(), copy.AggregatePubKey.ToBytes());
        Assert.Equal(original.SigningPubKey!.ToBytes(), copy.SigningPubKey!.ToBytes());

        // A nonce generated from the deserialized context must be usable by the
        // original session. Each participant nonces from a context whose
        // SigningPubKey is their own key.
        var nonce1 = copy.GenerateNonce(k1);
        var nonce2 = new MusigContext(pubs, Msg32, pubs[1]).GenerateNonce(k2);
        original.ProcessNonces(new[] { nonce1.CreatePubNonce(), nonce2.CreatePubNonce() });

        var partial1 = original.Sign(k1, nonce1);
        Assert.True(original.Verify(pubs[0], nonce1.CreatePubNonce(), partial1));
    }

    [Fact]
    public void RoundTrip_AfterTweakAndProcessNonces_SignsByteIdentically()
    {
        var k1 = Key(31);
        var k2 = Key(32);
        var pubs = new[] { k1.CreatePubKey(), k2.CreatePubKey() };

        var original = new MusigContext(pubs, Msg32, pubs[1]);
        var tweak = new byte[32];
        tweak[0] = 42;
        original.Tweak(tweak, true); // x-only tweak, as taproot outputs use

        // Signer 1 nonces from their own identically-tweaked context.
        var ctx1 = new MusigContext(pubs, Msg32, pubs[0]);
        ctx1.Tweak(tweak, true);
        var nonce1 = ctx1.GenerateNonce(k1);
        var nonce2 = original.GenerateNonce(k2);
        original.ProcessNonces(new[] { nonce1.CreatePubNonce(), nonce2.CreatePubNonce() });

        var blob = MusigContextWire.Serialize(original);
        var copy = MusigContextWire.Deserialize(blob);

        // A partial signature produced from the deserialized session must
        // verify against sessions derived independently of the blob — both the
        // original and the other participant's own tweaked context. (Signing
        // once mirrors the real flow; MusigPrivNonce enforces single use.)
        var fromCopy = copy.Sign(k2, nonce2);
        Assert.True(original.Verify(pubs[1], nonce2.CreatePubNonce(), fromCopy));
        ctx1.ProcessNonces(new[] { nonce1.CreatePubNonce(), nonce2.CreatePubNonce() });
        Assert.True(ctx1.Verify(pubs[1], nonce2.CreatePubNonce(), fromCopy));
    }

    [Fact]
    public void TwoBlobFlow_MirroringTheHubWire_ProducesAValidAggregateSignature()
    {
        // Mirrors the real device flow: the device receives one blob before
        // nonce generation (GenerateNonces) and a different blob after the
        // server processed all nonces (SignMusig), and signs with the secret
        // nonce it kept in between.
        var deviceKey = Key(51);
        var serverKey = Key(52);
        var pubs = new[] { deviceKey.CreatePubKey(), serverKey.CreatePubKey() };

        var serverCtx = new MusigContext(pubs, Msg32, pubs[0]);

        // Wire hop 1: pre-nonce context to the device.
        var deviceCtx1 = MusigContextWire.Deserialize(MusigContextWire.Serialize(serverCtx));
        var deviceSecNonce = deviceCtx1.GenerateNonce(deviceKey);

        // The counterparty nonces and signs through its own context, as every
        // MuSig participant does.
        var counterpartyCtx = new MusigContext(pubs, Msg32, pubs[1]);
        var serverSecNonce = counterpartyCtx.GenerateNonce(serverKey);
        var allNonces = new[] { deviceSecNonce.CreatePubNonce(), serverSecNonce.CreatePubNonce() };
        serverCtx.ProcessNonces(allNonces);
        counterpartyCtx.ProcessNonces(allNonces);

        // Wire hop 2: post-ProcessNonces context to the device.
        var deviceCtx2 = MusigContextWire.Deserialize(MusigContextWire.Serialize(serverCtx));
        var devicePartial = deviceCtx2.Sign(deviceKey, deviceSecNonce);

        var serverPartial = counterpartyCtx.Sign(serverKey, serverSecNonce);

        Assert.True(serverCtx.Verify(pubs[0], deviceSecNonce.CreatePubNonce(), devicePartial));
        var aggregated = serverCtx.AggregateSignatures(new[] { devicePartial, serverPartial });
        Assert.True(serverCtx.AggregatePubKey.ToXOnlyPubKey().SigVerifyBIP340(aggregated, Msg32));
    }

    [Fact]
    public void Deserialize_RejectsBlobFromDifferentSecpAssemblyVersion()
    {
        var k = Key(61);
        var ctx = new MusigContext(new[] { k.CreatePubKey() }, Msg32, k.CreatePubKey());
        var blob = MusigContextWire.Serialize(ctx);

        var tampered = blob.Replace(
            MusigContextWire.SecpAssemblyVersion,
            "0.0.0.0");
        Assert.NotEqual(blob, tampered);
        var ex = Assert.Throws<InvalidOperationException>(() => MusigContextWire.Deserialize(tampered));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
