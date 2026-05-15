using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.FullPipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Benchmarks.Authorization;

/// <summary>
/// Measures the authorization path cost in isolation. Covers the core
/// <see cref="ScopePairClaimAuthorizationProvider"/> matching algorithm at different
/// scope-pair counts — from single-scope queries to the S365 scale of 425 possible pairs.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AuthorizationBenchmarks
{
    // Provider configured like S365: CRUD hierarchy, "::" separator, no-scopes = full access
    private static readonly ScopePairClaimAuthorizationProvider _provider =
        ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: true,
            additionalIgnoredScopes: [
                "Signature365.ReadWrite.All",
                "Signature365ReadWrite",
                "Signature365Admin.ReadWrite.All"]);

    // Reuse an existing benchmark app and operation for the context parameter.
    private static readonly Validation.ValidationBenchmarksApp _app = new();

    private static readonly ApiOperationContext _dummyContext = new(
        new ServiceCollection().BuildServiceProvider(),
        _app,
        FpMinimalBenchmarkQuery.Configure(),
        CancellationToken.None);

    // Single required resolved scope pair (most common case: one [Resource] attribute)
    private static readonly ResolvedScopePair[] _singleRequired =
        [new ResolvedScopePair("Template", "Read", null, null)];

    // Multiple required resolved scope pairs (any-of: two [Resource] attributes)
    private static readonly ResolvedScopePair[] _multiRequired =
    [
        new ResolvedScopePair("Template", "Create", null, null),
        new ResolvedScopePair("Template", "Manage", null, null),
    ];

    // User with exact matching scope
    private static readonly ClaimsPrincipal _exactMatch = BuildPrincipal("Template::Read");

    // User with hierarchy match (Manage includes Read)
    private static readonly ClaimsPrincipal _hierarchyMatch = BuildPrincipal("Template::Manage");

    // User with no restrictive scopes (triggers allowWhenNoScopeClaims fast-path)
    private static readonly ClaimsPrincipal _noScopes = BuildPrincipal("Signature365.ReadWrite.All");

    // User with many scopes (simulates a real S365 user with multiple scope pairs)
    private static readonly ClaimsPrincipal _manyScopes = BuildPrincipal(
        "Template::Read",
        "Template::Create",
        "Template::Update",
        "Template::Delete",
        "SentEmail::Read",
        "SentEmail::Create",
        "Outlook::Manage",
        "Tenant::Read",
        "User::Manage",
        "Partner::Read",
        "Subscription::Read",
        "Subscription::Create",
        "Subscription::Update",
        "Billing::Read",
        "Billing::Create",
        "Import::Manage");

    // User who will fail authorization
    private static readonly ClaimsPrincipal _noMatch = BuildPrincipal("SentEmail::Read", "Outlook::Read");

    /// <summary>
    /// Exact scope match — the fastest happy-path: user holds exactly the required scope.
    /// </summary>
    /// <returns>The authorization result (always true for this scenario).</returns>
    [Benchmark(Baseline = true)]
    public Task<bool> ExactMatch_SingleScope()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _exactMatch, _singleRequired, default);
    }

    /// <summary>
    /// Hierarchy match — user holds Manage, operation requires Read.
    /// Exercises the verb expansion path.
    /// </summary>
    /// <returns>The authorization result (true via hierarchy match).</returns>
    [Benchmark]
    public Task<bool> HierarchyMatch_ManageIncludesRead()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _hierarchyMatch, _singleRequired, default);
    }

    /// <summary>
    /// No restrictive scopes — triggers the allowWhenNoScopeClaims fast-path.
    /// This is the most common S365 path (internal users with broad access).
    /// </summary>
    /// <returns>The authorization result (true via no-scopes fast-path).</returns>
    [Benchmark]
    public Task<bool> NoScopes_AllowFullAccess()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _noScopes, _singleRequired, default);
    }

    /// <summary>
    /// Multi-scope user, single required scope — exercises the linear scan
    /// over 16 user scopes to find the matching one.
    /// </summary>
    /// <returns>The authorization result after scanning all user scopes.</returns>
    [Benchmark]
    public Task<bool> ManyScopes_ExactMatch()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _manyScopes, _singleRequired, default);
    }

    /// <summary>
    /// Multi-scope user, multiple required scopes — exercises the any-of check
    /// across both user scopes and required scope pairs.
    /// </summary>
    /// <returns>The authorization result after cross-checking user and required scopes.</returns>
    [Benchmark]
    public Task<bool> ManyScopes_MultiRequired()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _manyScopes, _multiRequired, default);
    }

    /// <summary>
    /// Authorization failure — no matching scopes. Measures worst-case scan cost
    /// when all scope pairs are checked and none match.
    /// </summary>
    /// <returns>The authorization result (always false for this scenario).</returns>
    [Benchmark]
    public Task<bool> AuthorizationFailure()
    {
        return _provider.IsAuthorizedAsync(_dummyContext, _noMatch, _singleRequired, default);
    }

    private static ClaimsPrincipal BuildPrincipal(params string[] scopes)
    {
        var claims = new List<Claim>(scopes.Length + 1) { new("sub", "bench-user") };

        foreach (var scope in scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "BenchAuth"));
    }
}
