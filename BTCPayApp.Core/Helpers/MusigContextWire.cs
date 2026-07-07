using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin.Secp256k1;
using NBitcoin.Secp256k1.Musig;

namespace BTCPayApp.Core.Helpers;

/// <summary>
/// Serializes a <see cref="MusigContext"/> for transport across the app↔server
/// SignalR hub. SignalR's JSON protocol cannot move NBitcoin/secp types, and a
/// BTCPay plugin must not reconfigure the host's global SignalR protocol (it
/// would affect every other BTCPay hub), so the signing wire carries primitives
/// only — this blob is the MuSig2 session leg of that wire.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MusigContext"/> has no public serialization surface and its tweak
/// state is write-only, so this captures the full private field state via
/// reflection and rebuilds it with an uninitialized instance on the other side.
/// Both processes hold the same NBitcoin.Secp256k1 build (the plugin publish
/// layout guarantees a single copy server-side; the app compiles against the
/// same package), and <see cref="Deserialize"/> refuses blobs produced by a
/// different assembly version rather than risking silent field drift.
/// </para>
/// <para>
/// The proper long-term home for this is upstream: NArk's
/// IRemoteSignerTransport carrying a serializable signing-session DTO instead
/// of a live MusigContext. Until then this file owns the layout coupling.
/// </para>
/// </remarks>
public static class MusigContextWire
{
    public static readonly string SecpAssemblyVersion =
        typeof(MusigContext).Assembly.GetName().Version?.ToString() ?? "unknown";

    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Type ContextType = typeof(MusigContext);
    private static readonly Type SessionValuesType =
        typeof(MusigContext).Assembly.GetType("NBitcoin.Secp256k1.Musig.SessionValues")
        ?? throw new InvalidOperationException("NBitcoin.Secp256k1.Musig.SessionValues type not found — NBitcoin layout changed.");

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, AllInstance)
        ?? throw new InvalidOperationException($"Field '{name}' not found on {type.Name} — NBitcoin layout changed.");

    private static readonly FieldInfo PkHash = Field(ContextType, "pk_hash");
    private static readonly FieldInfo SecondPkX = Field(ContextType, "second_pk_x");
    private static readonly FieldInfo PkParity = Field(ContextType, "pk_parity");
    private static readonly FieldInfo ScalarTweak = Field(ContextType, "scalar_tweak");
    private static readonly FieldInfo Msg32 = Field(ContextType, "msg32");
    private static readonly FieldInfo Gacc = Field(ContextType, "gacc");
    private static readonly FieldInfo Tacc = Field(ContextType, "tacc");
    private static readonly FieldInfo SessionCache = Field(ContextType, "SessionCache");
    private static readonly FieldInfo AggregateNonce = Field(ContextType, "aggregateNonce");
    private static readonly FieldInfo AggregatePubKey = Field(ContextType, "aggregatePubKey");
    private static readonly FieldInfo Ctx = Field(ContextType, "ctx");
    private static readonly FieldInfo SigningPubKeyField = Field(ContextType, "<SigningPubKey>k__BackingField");
    private static readonly FieldInfo Adaptor = Field(ContextType, "adaptor");

    private static readonly FieldInfo SessionB = Field(SessionValuesType, "b");
    private static readonly FieldInfo SessionE = Field(SessionValuesType, "e");
    private static readonly FieldInfo SessionR = Field(SessionValuesType, "r");

    private sealed class Dto
    {
        public int V { get; set; } = 1;
        public string SecpVersion { get; set; } = "";
        public string? PkHash { get; set; }
        public string SecondPkX { get; set; } = "";
        public bool PkParity { get; set; }
        public string ScalarTweak { get; set; } = "";
        public string? Msg32 { get; set; }
        public string Gacc { get; set; } = "";
        public string Tacc { get; set; } = "";
        public SessionDto? Session { get; set; }
        public string? AggregateNonce { get; set; }
        public string? AggregatePubKey { get; set; }
        public string? SigningPubKey { get; set; }
        public string? Adaptor { get; set; }
    }

    private sealed class SessionDto
    {
        public string B { get; set; } = "";
        public string E { get; set; } = "";
        public string RX { get; set; } = "";
        public string RY { get; set; } = "";
        public bool RInfinity { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(MusigContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var dto = new Dto
        {
            SecpVersion = SecpAssemblyVersion,
            PkHash = HexOrNull((byte[]?)PkHash.GetValue(context)),
            SecondPkX = Hex(((FE)SecondPkX.GetValue(context)!).ToBytes()),
            PkParity = (bool)PkParity.GetValue(context)!,
            ScalarTweak = Hex(((Scalar)ScalarTweak.GetValue(context)!).ToBytes()),
            Msg32 = HexOrNull((byte[]?)Msg32.GetValue(context)),
            Gacc = Hex(((Scalar)Gacc.GetValue(context)!).ToBytes()),
            Tacc = Hex(((Scalar)Tacc.GetValue(context)!).ToBytes()),
            AggregateNonce = ((MusigPubNonce?)AggregateNonce.GetValue(context)) is { } nonce ? Hex(nonce.ToBytes()) : null,
            AggregatePubKey = ((ECPubKey?)AggregatePubKey.GetValue(context)) is { } agg ? Hex(agg.ToBytes()) : null,
            SigningPubKey = ((ECPubKey?)SigningPubKeyField.GetValue(context)) is { } spk ? Hex(spk.ToBytes()) : null,
            Adaptor = ((ECPubKey?)Adaptor.GetValue(context)) is { } adaptor ? Hex(adaptor.ToBytes()) : null,
        };

        if (SessionCache.GetValue(context) is { } session)
        {
            var r = (GE)SessionR.GetValue(session)!;
            dto.Session = new SessionDto
            {
                B = Hex(((Scalar)SessionB.GetValue(session)!).ToBytes()),
                E = Hex(((Scalar)SessionE.GetValue(session)!).ToBytes()),
                RX = Hex(r.x.ToBytes()),
                RY = Hex(r.y.ToBytes()),
                RInfinity = r.infinity,
            };
        }

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static MusigContext Deserialize(string blob)
    {
        ArgumentException.ThrowIfNullOrEmpty(blob);
        var dto = JsonSerializer.Deserialize<Dto>(blob, JsonOptions)
                  ?? throw new InvalidOperationException("MusigContext wire blob deserialized to null.");
        if (dto.V != 1)
            throw new InvalidOperationException($"Unsupported MusigContext wire version {dto.V}.");
        if (dto.SecpVersion != SecpAssemblyVersion)
            throw new InvalidOperationException(
                $"MusigContext wire blob was produced by NBitcoin.Secp256k1 {dto.SecpVersion} but this side runs {SecpAssemblyVersion}; " +
                "refusing to rehydrate reflected state across versions.");

        var context = (MusigContext)RuntimeHelpers.GetUninitializedObject(ContextType);
        PkHash.SetValue(context, FromHexOrNull(dto.PkHash));
        SecondPkX.SetValue(context, new FE(FromHex(dto.SecondPkX)));
        PkParity.SetValue(context, dto.PkParity);
        ScalarTweak.SetValue(context, new Scalar(FromHex(dto.ScalarTweak)));
        Msg32.SetValue(context, FromHexOrNull(dto.Msg32));
        Gacc.SetValue(context, new Scalar(FromHex(dto.Gacc)));
        Tacc.SetValue(context, new Scalar(FromHex(dto.Tacc)));
        AggregateNonce.SetValue(context, dto.AggregateNonce is null ? null : new MusigPubNonce(FromHex(dto.AggregateNonce)));
        AggregatePubKey.SetValue(context, dto.AggregatePubKey is null ? null : ECPubKey.Create(FromHex(dto.AggregatePubKey)));
        SigningPubKeyField.SetValue(context, dto.SigningPubKey is null ? null : ECPubKey.Create(FromHex(dto.SigningPubKey)));
        Adaptor.SetValue(context, dto.Adaptor is null ? null : ECPubKey.Create(FromHex(dto.Adaptor)));
        Ctx.SetValue(context, Context.Instance);

        if (dto.Session is { } sessionDto)
        {
            var session = Activator.CreateInstance(SessionValuesType, nonPublic: true)
                          ?? throw new InvalidOperationException("Could not instantiate SessionValues.");
            SessionB.SetValue(session, new Scalar(FromHex(sessionDto.B)));
            SessionE.SetValue(session, new Scalar(FromHex(sessionDto.E)));
            SessionR.SetValue(session, new GE(new FE(FromHex(sessionDto.RX)), new FE(FromHex(sessionDto.RY)), sessionDto.RInfinity));
            SessionCache.SetValue(context, session);
        }

        return context;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
    private static string? HexOrNull(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(bytes);
    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);
    private static byte[]? FromHexOrNull(string? hex) => hex is null ? null : Convert.FromHexString(hex);
}
