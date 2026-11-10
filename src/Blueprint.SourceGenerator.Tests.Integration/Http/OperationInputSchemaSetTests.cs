using System.Text.Json;
using Blueprint.Runtime.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves an operation's input schema is had by its exact name when another operation's name differs from it only by case.
/// </summary>
/// <remarks>
/// Descriptor names are unique by ordinal comparison, so <c>GetFoo</c> and <c>Getfoo</c> are both valid, in namespaces of
/// their own. They cannot be declared in the integration app, whose typed HTTP client and descriptor generators give each
/// operation a source file of its name, and Roslyn compares source file names ignoring case, so the set is made from
/// schemas here.
/// </remarks>
public sealed class OperationInputSchemaSetTests
{
    [Fact]
    public void Two_operations_whose_names_differ_only_by_case_are_each_had_by_their_exact_name()
    {
        var set = OperationInputSchemaSet.FromSchemas([Schema("GetFoo"), Schema("Getfoo"), Schema("AddNote")]);

        set.GetSchema("GetFoo")!.OperationId.Should().Be("GetFoo-id");
        set.GetSchema("Getfoo")!.OperationId.Should().Be("Getfoo-id");
        set.GetSchema("AddNote")!.OperationId.Should().Be("AddNote-id");
        set.GetSchemas().Select(static s => s.OperationName).Should().Equal("AddNote", "GetFoo", "Getfoo");
    }

    [Fact]
    public void A_name_two_operations_match_only_ignoring_case_fails_naming_both()
    {
        var set = OperationInputSchemaSet.FromSchemas([Schema("GetFoo"), Schema("Getfoo"), Schema("AddNote")]);

        var act = () => set.GetSchema("getfoo");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'getfoo'*GetFoo*Getfoo*");
        set.GetSchema("addnote")!.OperationName.Should().Be("AddNote", "a name only one operation matches ignoring case is still had");
        set.GetSchema("NoSuchOperation").Should().BeNull();
    }

    private static OperationInputSchema Schema(string operationName)
    {
        using var document = JsonDocument.Parse("""{"type":"object"}""");

        return new OperationInputSchema(operationName, $"{operationName}-id", document.RootElement.Clone(), []);
    }
}
