using Blueprint.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// <see cref="VerbHierarchy"/> is the transitive closure of an app's verb inclusions, read both ways: the verbs a verb
/// includes, and the verbs that include it (Symprex/blueprint#129).
/// </summary>
public sealed class VerbHierarchyTests
{
    private static readonly Dictionary<string, string[]> _crud = new()
    {
        ["Manage"] = ["Create", "Read", "Update", "Delete"],
    };

    [Fact]
    public void A_parent_includes_its_listed_verbs_and_each_is_included_in_it()
    {
        var hierarchy = new VerbHierarchy(_crud, StringComparison.Ordinal);

        hierarchy.Includes("Manage").Should().BeEquivalentTo("Create", "Read", "Update", "Delete");
        hierarchy.IncludedIn("Read").Should().BeEquivalentTo("Manage");
        hierarchy.IncludedIn("Manage").Should().BeEmpty();
        hierarchy.Includes("Read").Should().BeEmpty();
    }

    [Fact]
    public void Inclusion_is_transitive()
    {
        var hierarchy = new VerbHierarchy(
            new Dictionary<string, string[]> { ["Admin"] = ["Manage"], ["Manage"] = ["Read"] },
            StringComparison.Ordinal);

        hierarchy.Includes("Admin").Should().BeEquivalentTo("Manage", "Read");
        hierarchy.IncludedIn("Read").Should().BeEquivalentTo("Manage", "Admin");
    }

    [Fact]
    public void A_cycle_ends_the_walk_and_no_verb_is_listed_as_including_itself()
    {
        var hierarchy = new VerbHierarchy(
            new Dictionary<string, string[]> { ["A"] = ["B"], ["B"] = ["C"], ["C"] = ["A"] },
            StringComparison.Ordinal);

        hierarchy.Includes("A").Should().BeEquivalentTo("B", "C");
        hierarchy.IncludedIn("A").Should().BeEquivalentTo("B", "C");
    }

    [Fact]
    public void A_verb_the_hierarchy_does_not_name_includes_and_is_included_in_nothing()
    {
        var hierarchy = new VerbHierarchy(_crud, StringComparison.Ordinal);

        hierarchy.Includes("Archive").Should().BeEmpty();
        hierarchy.IncludedIn("Archive").Should().BeEmpty();
        hierarchy.Contains("Archive").Should().BeFalse();
    }

    [Fact]
    public void Contains_every_verb_named_as_a_parent_or_a_child_including_a_parent_with_no_children()
    {
        var hierarchy = new VerbHierarchy(
            new Dictionary<string, string[]> { ["Manage"] = ["Read"], ["Archive"] = [] },
            StringComparison.Ordinal);

        hierarchy.Contains("Manage").Should().BeTrue();
        hierarchy.Contains("Read").Should().BeTrue();
        hierarchy.Contains("Archive").Should().BeTrue();
    }

    [Fact]
    public void Verbs_are_compared_as_the_comparison_given()
    {
        var ordinal = new VerbHierarchy(_crud, StringComparison.Ordinal);
        var ignoreCase = new VerbHierarchy(_crud, StringComparison.OrdinalIgnoreCase);

        ordinal.IncludedIn("read").Should().BeEmpty();
        ordinal.Contains("read").Should().BeFalse();
        ignoreCase.IncludedIn("read").Should().BeEquivalentTo("Manage");
        ignoreCase.Includes("MANAGE").Should().HaveCount(4);
        ignoreCase.Contains("read").Should().BeTrue();
        ignoreCase.Comparison.Should().Be(StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parents_differing_only_in_case_are_merged_under_a_case_insensitive_comparison()
    {
        var hierarchy = new VerbHierarchy(
            new Dictionary<string, string[]> { ["Manage"] = ["Read"], ["manage"] = ["Delete"] },
            StringComparison.OrdinalIgnoreCase);

        hierarchy.Includes("Manage").Should().BeEquivalentTo("Read", "Delete");
    }

    [Fact]
    public void An_empty_hierarchy_includes_nothing()
    {
        var hierarchy = new VerbHierarchy(new Dictionary<string, string[]>(), StringComparison.Ordinal);

        hierarchy.Includes("Manage").Should().BeEmpty();
        hierarchy.IncludedIn("Read").Should().BeEmpty();
    }

    [Fact]
    public void The_inclusions_are_required()
    {
        var create = () => new VerbHierarchy(null!, StringComparison.Ordinal);

        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_provider_refuses_a_hierarchy_compared_other_than_ordinally_as_it_refuses_such_a_scope_comparison()
    {
        var create = () => new ScopePairClaimAuthorizationProvider(new VerbHierarchy(_crud, StringComparison.InvariantCulture), "scope", "::");

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_provider_requires_the_hierarchy()
    {
        var create = () => new ScopePairClaimAuthorizationProvider((VerbHierarchy)null!, "scope", "::");

        create.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("verbHierarchy");
    }
}
