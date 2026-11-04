using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// <see cref="ScopePairOperationIndex"/> answers, from the operations' declared scope pairs and the app's
/// <see cref="VerbHierarchy"/>, which operations a held scope pair satisfies and which pairs satisfy an operation, as
/// <see cref="ScopePairClaimAuthorizationProvider"/> would decide it (Symprex/blueprint#129).
/// </summary>
public sealed class ScopePairOperationIndexTests
{
    private static readonly Dictionary<string, string[]> _inclusions = new()
    {
        ["Admin"] = ["Manage"],
        ["Manage"] = ["Create", "Read", "Update", "Delete"],
    };

    private static readonly IntegrationTestBlueprintApp _app = new();

    [Fact]
    public void A_pair_satisfies_the_operations_declaring_it()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        OperationTypes(index.OperationsFor(new ScopePair("indexWidget", "Read"))).Should().BeEquivalentTo(
            [typeof(IndexReadCommand), typeof(IndexTwoPairCommand), typeof(IndexInheritingCommand)]);
    }

    [Fact]
    public void A_pair_satisfies_the_operations_declaring_a_verb_it_includes_transitively()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        var expected = new[] { typeof(IndexReadCommand), typeof(IndexTwoPairCommand), typeof(IndexInheritingCommand), typeof(IndexManageCommand) };

        OperationTypes(index.OperationsFor(new ScopePair("indexWidget", "Manage"))).Should().BeEquivalentTo(expected);
        OperationTypes(index.OperationsFor(new ScopePair("indexWidget", "Admin"))).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void A_narrower_verb_does_not_satisfy_a_broader_one()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        OperationTypes(index.OperationsFor(new ScopePair("indexWidget", "Read"))).Should().NotContain(typeof(IndexManageCommand));
    }

    [Fact]
    public void A_verb_outside_the_hierarchy_is_satisfied_only_by_itself()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        OperationTypes(index.OperationsFor(new ScopePair("indexGadget", "Archive"))).Should().Equal(typeof(IndexTwoPairCommand));
        index.OperationsFor(new ScopePair("indexGadget", "Admin")).Should().BeEmpty();
    }

    [Fact]
    public void The_template_of_the_pair_asked_about_is_ignored()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        OperationTypes(index.OperationsFor(new ScopePair("indexGadget", "Archive", "/anything/{Id}"))).Should().Equal(typeof(IndexTwoPairCommand));
    }

    [Fact]
    public void A_pair_no_operation_declares_satisfies_none()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        index.OperationsFor(new ScopePair("nothing", "Read")).Should().BeEmpty();
    }

    [Fact]
    public void Nouns_and_verbs_are_compared_as_the_hierarchy_compares_verbs()
    {
        CreateIndex(StringComparison.Ordinal).OperationsFor(new ScopePair("INDEXGADGET", "archive")).Should().BeEmpty();

        OperationTypes(CreateIndex(StringComparison.OrdinalIgnoreCase).OperationsFor(new ScopePair("INDEXGADGET", "archive")))
            .Should().Equal(typeof(IndexTwoPairCommand));
    }

    [Fact]
    public void Given_the_separator_a_pair_matches_as_its_scope_value_does_wherever_the_separator_splits_it()
    {
        // The provider accepts the scope "indexTemplate::Admin::Read" for the declared pair (indexTemplate, Admin::Read),
        // so a pair joining to that value satisfies the operation however it is split.
        var index = new ScopePairOperationIndex(_app, new VerbHierarchy(_inclusions, StringComparison.Ordinal), separator: "::");

        OperationTypes(index.OperationsFor(new ScopePair("indexTemplate::Admin", "Read"))).Should().Equal(typeof(IndexSeparatorVerbCommand));
        OperationTypes(index.OperationsFor(new ScopePair("indexTemplate", "Admin::Read"))).Should().Equal(typeof(IndexSeparatorVerbCommand));
    }

    [Fact]
    public void Without_the_separator_a_pair_matches_on_its_noun_and_verb_as_given()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        index.OperationsFor(new ScopePair("indexTemplate::Admin", "Read")).Should().BeEmpty();
        OperationTypes(index.OperationsFor(new ScopePair("indexTemplate", "Admin::Read"))).Should().Equal(typeof(IndexSeparatorVerbCommand));
    }

    [Fact]
    public void An_operation_is_satisfied_by_each_declared_pair_and_each_verb_that_includes_it_without_templates()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        index.SatisfiedBy(_app.FindOperation(typeof(IndexTwoPairCommand))).Should().BeEquivalentTo(
        [
            new ScopePair("indexWidget", "Read"),
            new ScopePair("indexWidget", "Manage"),
            new ScopePair("indexWidget", "Admin"),
            new ScopePair("indexGadget", "Archive"),
        ]);
    }

    [Fact]
    public void An_operation_with_no_scope_pairs_is_satisfied_by_none()
    {
        var index = CreateIndex(StringComparison.Ordinal);

        index.SatisfiedBy(_app.FindOperation(typeof(IndexUnpermissionedCommand))).Should().BeEmpty();
        OperationTypes(index.OperationsFor(new ScopePair("indexWidget", "Admin"))).Should().NotContain(typeof(IndexUnpermissionedCommand));
    }

    [Fact]
    public void A_verb_outside_the_hierarchy_is_refused_at_construction_when_the_app_asks()
    {
        var create = () => new ScopePairOperationIndex(_app, new VerbHierarchy(_inclusions, StringComparison.Ordinal), requireKnownVerbs: true);

        create.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("indexGadget").And.Contain("Archive").And.Contain(nameof(IndexTwoPairCommand));
    }

    [Fact]
    public void Every_declared_verb_in_the_hierarchy_passes_the_construction_check()
    {
        // Each verb the app declares, named as a parent including nothing, read from the attributes' feature data here
        var leaves = _app.Operations
            .SelectMany(static o => o.TryGetFeatureData<OperationAuthorizationFeatureData>(out var a) ? a.ScopePairs : [])
            .Select(static p => p.Verb)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(static v => v, static _ => Array.Empty<string>(), StringComparer.Ordinal);

        var create = () => new ScopePairOperationIndex(_app, new VerbHierarchy(leaves, StringComparison.Ordinal), requireKnownVerbs: true);

        create.Should().NotThrow();
    }

    [Fact]
    public void The_app_and_the_hierarchy_are_required()
    {
        var hierarchy = new VerbHierarchy(_inclusions, StringComparison.Ordinal);

        ((Func<ScopePairOperationIndex>)(() => new ScopePairOperationIndex(null!, hierarchy))).Should().Throw<ArgumentNullException>();
        ((Func<ScopePairOperationIndex>)(() => new ScopePairOperationIndex(_app, null!))).Should().Throw<ArgumentNullException>();
    }

    // For every operation in the integration app and every noun and verb it or the hierarchy names, the index and the
    // provider, given the same hierarchy, agree whether a token holding only that pair may run the operation.
    [Theory]
    [InlineData(StringComparison.Ordinal)]
    [InlineData(StringComparison.OrdinalIgnoreCase)]
    public async Task The_index_agrees_with_the_providers_decision_for_every_pair_in_the_app(StringComparison comparison)
    {
        var inclusions = new Dictionary<string, string[]>
        {
            ["Admin"] = ["Manage", "rename", "archive"],
            ["Manage"] = ["Create", "Read", "Update", "Delete"],
            ["manage"] = ["read", "update"],
            ["write"] = ["read"],
        };

        var hierarchy = new VerbHierarchy(inclusions, comparison);
        var index = new ScopePairOperationIndex(_app, hierarchy, separator: "::");

        IResourceAuthorizationProvider[] providers =
        [
            new ScopePairClaimAuthorizationProvider(hierarchy, "scope", "::"),
            new ScopePairClaimAuthorizationProvider("scope", "::", verbInclusions: inclusions, scopeComparison: comparison),
        ];

        var operations = _app.Operations
            .Where(static o => o.TryGetFeatureData<OperationAuthorizationFeatureData>(out var a) && !a.ScopePairs.IsDefaultOrEmpty)
            .ToList();

        operations.Should().Contain(o => o.OperationType == typeof(IndexTwoPairCommand));

        var declared = operations.SelectMany(static o => o.GetFeatureData<OperationAuthorizationFeatureData>()!.ScopePairs).ToList();
        var nouns = declared.Select(static p => p.Noun).SelectMany(static n => new[] { n, n.ToUpperInvariant() }).Distinct().ToList();
        var verbs = declared.Select(static p => p.Verb)
            .Concat(inclusions.Keys)
            .Concat(inclusions.Values.SelectMany(static v => v))
            .SelectMany(static v => new[] { v, v.ToUpperInvariant() })
            .Distinct()
            .ToList();

        // Every noun and verb, plus each scope value that contains the separator split again at each of its separators,
        // as a noun or verb containing the separator is a valid component the provider matches on the joined value.
        var pairs = nouns.SelectMany(n => verbs.Select(v => (Noun: n, Verb: v))).ToList();

        pairs.AddRange(pairs
            .Select(static p => p.Noun + "::" + p.Verb)
            .SelectMany(static value => Enumerable.Range(1, value.Length - 2)
                .Where(i => value.AsSpan(i).StartsWith("::"))
                .Select(i => (Noun: value[..i], Verb: value[(i + 2)..])))
            .Where(static p => p.Noun.Length > 0 && p.Verb.Length > 0)
            .ToList());

        pairs = [.. pairs.Distinct()];

        pairs.Should().Contain(("indexTemplate::Admin", "Read"));

        var mismatches = new List<string>();
        var allowedCount = 0;

        foreach (var operation in operations)
        {
            var request = ResourceAuthorizationRequest.Execute(
                operation,
                [.. operation.GetFeatureData<OperationAuthorizationFeatureData>()!.ScopePairs.Select(static p => new ResolvedScopePair(p.Noun, p.Verb, p.ResourceKeyTemplate, null))]);

            var satisfiedBy = index.SatisfiedBy(operation);

            foreach (var (noun, verb) in pairs)
            {
                var pair = new ScopePair(noun, verb);
                var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", $"{noun}::{verb}")], "test"));

                var inOperationsFor = index.OperationsFor(pair).Contains(operation);
                var inSatisfiedBy = satisfiedBy.Any(s => string.Equals($"{s.Noun}::{s.Verb}", $"{noun}::{verb}", comparison));

                foreach (var provider in providers)
                {
                    var allowed = (await provider.AuthorizeAsync(null!, principal, request, CancellationToken.None)).IsAllowed;

                    if (allowed != inOperationsFor || allowed != inSatisfiedBy)
                    {
                        mismatches.Add($"{operation.OperationType.Name} {noun}::{verb}: provider {allowed}, OperationsFor {inOperationsFor}, SatisfiedBy {inSatisfiedBy}");
                    }

                    allowedCount += allowed ? 1 : 0;
                }
            }
        }

        mismatches.Should().BeEmpty();
        allowedCount.Should().BeGreaterThan(operations.Count * providers.Length, "every operation is allowed by at least its own pair, and some by an including verb");
    }

    private static ScopePairOperationIndex CreateIndex(StringComparison comparison)
    {
        return new ScopePairOperationIndex(_app, new VerbHierarchy(_inclusions, comparison));
    }

    private static IEnumerable<Type> OperationTypes(IEnumerable<ApiOperationDescriptor> descriptors)
    {
        return descriptors.Select(static d => d.OperationType);
    }
}

/// <summary>Reads index widgets.</summary>
[Resource("indexWidget", "Read")]
public partial class IndexReadCommand : ICommand<string>
{
    public static string Handle(IndexReadCommand command)
    {
        return "executed";
    }
}

/// <summary>Manages index widgets.</summary>
[Resource("indexWidget", "Manage", "/widgets/{Id}")]
public partial class IndexManageCommand : ICommand<string>
{
    public string? Id { get; set; }

    public static string Handle(IndexManageCommand command)
    {
        return "executed";
    }
}

/// <summary>Reads an index widget or archives an index gadget, either satisfying it.</summary>
[Resource("indexWidget", "Read", "/widgets/{Id}")]
[Resource("indexGadget", "Archive")]
public partial class IndexTwoPairCommand : ICommand<string>
{
    public string? Id { get; set; }

    public static string Handle(IndexTwoPairCommand command)
    {
        return "executed";
    }
}

/// <summary>Inherits <see cref="IndexReadCommand"/>'s pair.</summary>
[FromResource(typeof(IndexReadCommand))]
public partial class IndexInheritingCommand : ICommand<string>
{
    public static string Handle(IndexInheritingCommand command)
    {
        return "executed";
    }
}

/// <summary>Declares a verb containing the scope separator, which the provider matches on the joined scope value.</summary>
[Resource("indexTemplate", "Admin::Read")]
public partial class IndexSeparatorVerbCommand : ICommand<string>
{
    public static string Handle(IndexSeparatorVerbCommand command)
    {
        return "executed";
    }
}

/// <summary>Declares no scope pair.</summary>
public partial class IndexUnpermissionedCommand : ICommand<string>
{
    public static string Handle(IndexUnpermissionedCommand command)
    {
        return "executed";
    }
}
