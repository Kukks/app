using BTCPayServer.Client;

namespace BTCPayApp.Core.Helpers;

/// <summary>
/// Client-side reimplementation of BTCPay Server's permission policy containment, mirrored from
/// <c>BTCPayServer.Services.PermissionService</c> and the built-in <c>PolicyDefinition</c> hierarchy
/// registered in <c>BTCPayServerServices.AddPolicyDefinitions</c> on master.
///
/// The app authorizes Greenfield claims on the device, where the server's <c>PermissionService</c>
/// (built from DI-registered <c>PolicyDefinition</c>s) is not available, so the built-in policy tree
/// and the containment algorithm are reproduced here. This replaces the
/// <c>PermissionSet.Contains(policy, store)</c> / <c>Policies.PolicyMap</c> helpers that earlier
/// versions of <c>BTCPayServer.Client</c> exposed but current master no longer ships.
///
/// SECURITY REVIEW (2026-05-29 vs btcpayserver master):
/// <list type="bullet">
/// <item>Policy hierarchy matches the 15 parent/child entries + 17 standalone entries +
/// <see cref="Policies.Unrestricted"/> root in
/// <c>BTCPayServerServices.AddPolicyDefinitions</c> verbatim.</item>
/// <item>Containment algorithm matches <c>PermissionService.ContainsPolicy</c>: a granted
/// policy contains a requested one iff the granted policy appears in the requested policy's
/// ancestor chain (with Unrestricted as the root of every tree).</item>
/// <item>Per-permission <c>Contains</c> matches <c>PermissionService.Contains</c>: policy
/// containment AND (null scope OR scope-equality). The <c>anyScope</c> param btcpayserver
/// exposes is not needed here — the only caller is
/// <c>AuthorizationHandler</c>, which always supplies a concrete store id, so we drop it.</item>
/// </list>
/// If btcpayserver master adds, removes, or re-parents any built-in policy, the
/// <see cref="IncludedPermissions"/> map and the <see cref="StandalonePolicies"/> array MUST
/// be updated here. The submodule pin records which btcpayserver commit was the basis of the
/// last review — re-run the diff against the current submodule HEAD when bumping it.
/// </summary>
public static class PermissionContainment
{
    // Parent policy -> directly included (child) policies.
    // Source of truth: BTCPayServerServices.AddPolicyDefinitions (btcpayserver master).
    // Case-insensitive to match PermissionService's definitionsByPermission (OrdinalIgnoreCase).
    private static readonly Dictionary<string, string[]> IncludedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Policies.CanModifyInvoices] = new[]
        {
            Policies.CanViewInvoices,
            Policies.CanCreateInvoice,
            Policies.CanCreateLightningInvoiceInStore
        },
        [Policies.CanModifyServerSettings] = new[]
        {
            Policies.CanUseInternalLightningNode,
            Policies.CanManageUsers
        },
        [Policies.CanModifyStoreSettings] = new[]
        {
            Policies.CanManagePullPayments,
            Policies.CanModifyInvoices,
            Policies.CanViewStoreSettings,
            Policies.CanModifyWebhooks,
            Policies.CanModifyPaymentRequests,
            Policies.CanManagePayouts,
            Policies.CanUseLightningNodeInStore,
            Policies.CanSendStoreEmail
        },
        [Policies.CanViewStoreSettings] = new[]
        {
            Policies.CanViewInvoices,
            Policies.CanViewPaymentRequests,
            Policies.CanViewReports,
            Policies.CanViewPullPayments,
            Policies.CanViewPayouts
        },
        [Policies.CanModifyPaymentRequests] = new[] { Policies.CanViewPaymentRequests },
        [Policies.CanModifyProfile] = new[] { Policies.CanViewProfile },
        [Policies.CanManageNotificationsForUser] = new[] { Policies.CanViewNotificationsForUser },
        [Policies.CanUseInternalLightningNode] = new[]
        {
            Policies.CanCreateLightningInvoiceInternalNode,
            Policies.CanViewLightningInvoiceInternalNode
        },
        [Policies.CanUseLightningNodeInStore] = new[]
        {
            Policies.CanViewLightningInvoiceInStore,
            Policies.CanCreateLightningInvoiceInStore
        },
        [Policies.CanCreateLightningInvoiceInStore] = new[] { Policies.CanViewLightningInvoiceInStore },
        [Policies.CanManagePullPayments] = new[]
        {
            Policies.CanCreatePullPayments,
            Policies.CanArchivePullPayments
        },
        [Policies.CanCreatePullPayments] = new[] { Policies.CanCreateNonApprovedPullPayments },
        [Policies.CanCreateNonApprovedPullPayments] = new[] { Policies.CanViewPullPayments },
        [Policies.CanManageUsers] = new[] { Policies.CanCreateUser },
        [Policies.CanManagePayouts] = new[] { Policies.CanViewPayouts }
    };

    // Leaf/standalone built-in policies that appear as definitions but have no children of their own.
    private static readonly string[] StandalonePolicies =
    {
        Policies.CanViewInvoices,
        Policies.CanCreateInvoice,
        Policies.CanModifyWebhooks,
        Policies.CanSendStoreEmail,
        Policies.CanViewReports,
        Policies.CanViewPaymentRequests,
        Policies.CanViewProfile,
        Policies.CanViewUsers,
        Policies.CanCreateUser,
        Policies.CanDeleteUser,
        Policies.CanViewNotificationsForUser,
        Policies.CanViewLightningInvoiceInternalNode,
        Policies.CanCreateLightningInvoiceInternalNode,
        Policies.CanViewLightningInvoiceInStore,
        Policies.CanArchivePullPayments,
        Policies.CanViewPullPayments,
        Policies.CanViewPayouts
    };

    // policy -> set of ancestor policies (including itself), with Unrestricted as the root of every tree.
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Ancestors = BuildAncestors();

    /// <summary>All built-in policies, including <see cref="Policies.Unrestricted"/>.</summary>
    public static IReadOnlyCollection<string> AllPolicies { get; } = Ancestors.Keys.ToArray();

    private static IReadOnlyDictionary<string, HashSet<string>> BuildAncestors()
    {
        // Collect every policy that participates in the built-in definitions.
        var allPolicies = new HashSet<string>(StandalonePolicies, StringComparer.OrdinalIgnoreCase) { Policies.Unrestricted };
        foreach (var (parent, included) in IncludedPermissions)
        {
            allPolicies.Add(parent);
            foreach (var child in included)
                allPolicies.Add(child);
        }

        // parent -> children, mirroring PermissionService's node graph.
        var childrenByPolicy = allPolicies.ToDictionary(p => p, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var (parent, included) in IncludedPermissions)
        {
            foreach (var child in included)
                childrenByPolicy[parent].Add(child);
        }

        // Anything that is not included by another policy becomes a child of Unrestricted,
        // matching PermissionService's constructor (orphans attach under Unrestricted).
        var hasParent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var included in childrenByPolicy.Values)
        {
            foreach (var child in included)
                hasParent.Add(child);
        }
        foreach (var policy in allPolicies)
        {
            if (string.Equals(policy, Policies.Unrestricted, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!hasParent.Contains(policy))
                childrenByPolicy[Policies.Unrestricted].Add(policy);
        }

        // Transitively walk parents for each policy to get its ancestor set (including itself).
        var ancestors = allPolicies.ToDictionary(p => p, p => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { p }, StringComparer.OrdinalIgnoreCase);
        foreach (var policy in allPolicies)
        {
            var stack = new Stack<string>();
            stack.Push(policy);
            var visited = new HashSet<string> { policy };
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var (parent, childSet) in childrenByPolicy)
                {
                    if (!childSet.Contains(current) || !visited.Add(parent))
                        continue;
                    ancestors[policy].Add(parent);
                    stack.Push(parent);
                }
            }
        }
        return ancestors;
    }

    /// <summary>
    /// True if <paramref name="policy"/> is contained by (equal to or an ancestor of)
    /// <paramref name="subpolicy"/> in the built-in policy tree. Mirrors
    /// <c>PermissionService.ContainsPolicy</c>: both policies must be known, and the sub-policy's
    /// ancestor chain must include the policy (with <c>Unrestricted</c> containing everything).
    /// </summary>
    private static bool ContainsPolicy(string policy, string subpolicy)
    {
        if (!Ancestors.TryGetValue(policy, out _) || !Ancestors.TryGetValue(subpolicy, out var subAncestors))
            return false;
        return subAncestors.Contains(policy);
    }

    /// <summary>
    /// True if a single granted <paramref name="permission"/> covers the requested
    /// <paramref name="requestedPolicy"/> on the given <paramref name="store"/>. Mirrors
    /// <c>PermissionService.Contains</c> (and the historical <c>Permission.Contains</c>): the granted
    /// policy must contain the requested one, and for store-scoped grants the scope must be
    /// unrestricted (null) or match the requested store.
    /// </summary>
    private static bool Contains(Permission permission, string requestedPolicy, string store)
    {
        if (!ContainsPolicy(permission.Policy, requestedPolicy))
            return false;
        return permission.Scope == null || permission.Scope == store;
    }

    /// <summary>
    /// Faithful replacement for the removed <c>PermissionSet.Contains(policy, store)</c>: true when any
    /// permission in the set grants <paramref name="requestedPolicy"/> for <paramref name="store"/>.
    /// </summary>
    public static bool ContainsPolicy(this PermissionSet permissionSet, string requestedPolicy, string store)
    {
        ArgumentNullException.ThrowIfNull(requestedPolicy);
        ArgumentNullException.ThrowIfNull(store);
        return permissionSet.Permissions.Any(p => Contains(p, requestedPolicy, store));
    }
}
