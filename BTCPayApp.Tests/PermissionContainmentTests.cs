using System.Reflection;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Client;
using Xunit;

namespace BTCPayApp.Tests;

public class PermissionContainmentTests
{
    /// <summary>
    /// <see cref="PermissionContainment"/> reimplements btcpayserver's permission set
    /// client-side (the client library no longer ships the containment hierarchy). The PR
    /// commits to re-verifying it whenever btcpayserver adds or removes a built-in policy;
    /// this test automates the "adds/removes a policy" half of that guard by asserting every
    /// policy constant in <see cref="Policies"/> is known to <see cref="PermissionContainment"/>.
    ///
    /// It deliberately does NOT assert containment *semantics* (whether policy A contains B):
    /// that reference implementation lives in the main BTCPayServer web project, which the app
    /// does not reference (only BTCPayServer.Client), so semantic parity stays a documented
    /// manual review (see PermissionContainment's XML header).
    /// </summary>
    [Fact]
    public void AllPolicies_CoversEveryBtcpayserverPolicyConstant()
    {
        var known = PermissionContainment.AllPolicies.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = typeof(Policies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            // Real store/server authz policies only. "Unscoped" variants end in ':' — the same
            // base policy carrying an explicit empty scope, resolved by ContainsPolicy's store
            // argument rather than a distinct entry in the hierarchy, so they are not expected
            // as separate AllPolicies members.
            .Where(v => v.StartsWith("btcpay.", StringComparison.Ordinal) &&
                        !v.EndsWith(":", StringComparison.Ordinal))
            .Where(v => !known.Contains(v))
            .ToArray();

        Assert.True(missing.Length == 0,
            "PermissionContainment is out of sync with BTCPayServer.Client.Policies (drift). " +
            "Add these policies to PermissionContainment (and re-verify the containment hierarchy " +
            "against btcpayserver's PermissionService): " + string.Join(", ", missing));
    }
}
