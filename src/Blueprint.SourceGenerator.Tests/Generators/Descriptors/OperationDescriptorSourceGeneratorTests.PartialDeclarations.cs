using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

/// <summary>
/// Tests that an operation's descriptor is re-planned when a declaration other than the one the generator discovered it by
/// changes — another partial declaration of the operation, or a base type, in a file of its own — while the operation's own
/// file is unedited, as an IDE keeps an unedited document's syntax tree between runs.
/// </summary>
public class OperationDescriptorSourceGeneratorPartialDeclarationTests
{
    private const string Preamble = "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
        + "namespace TestNamespace;\n\n";

    [Fact]
    public void When_a_query_property_in_another_partial_declaration_changes_then_the_descriptor_is_replanned()
    {
        var operationFile = ("GetWidgetQuery.cs", Preamble + """
            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var runs = TestHelpers.CompileWithTrackedGeneratorsAcrossRevisions(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            [operationFile, PartialFile("public int Page { get; set; }")],
            [operationFile, PartialFile("public string Size { get; set; }")],
            [operationFile, PartialFile(string.Empty)]);

        AssertQueryProperties(runs, "GetWidgetQuery");

        static (string FilePath, string Source) PartialFile(string members)
        {
            return ("GetWidgetQuery.Paging.cs", Preamble + $$"""
                public partial class GetWidgetQuery
                {
                    {{members}}
                }
                """);
        }
    }

    [Fact]
    public void When_a_query_property_in_a_base_type_in_another_file_changes_then_the_descriptor_is_replanned()
    {
        var operationFile = ("GetWidgetQuery.cs", Preamble + """
            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : WidgetQueryBase, IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var runs = TestHelpers.CompileWithTrackedGeneratorsAcrossRevisions(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            [operationFile, BaseFile("public int Page { get; set; }")],
            [operationFile, BaseFile("public string Size { get; set; }")],
            [operationFile, BaseFile(string.Empty)]);

        AssertQueryProperties(runs, "GetWidgetQuery");

        static (string FilePath, string Source) BaseFile(string members)
        {
            return ("WidgetQueryBase.cs", Preamble + $$"""
                public abstract class WidgetQueryBase
                {
                    {{members}}
                }
                """);
        }
    }

    private static void AssertQueryProperties(IReadOnlyList<MultiGeneratorGenerationResult> runs, string operationName)
    {
        // A replayed plan names a property the operation no longer has, which fails the build with CS1061.
        runs.Should().AllSatisfy(static run => run.Diagnostics.Should().BeEmpty());

        DescriptorSourceOf(runs[0], operationName).Should().Contain("typed.Page").And.NotContain("typed.Size");
        DescriptorSourceOf(runs[1], operationName).Should().Contain("typed.Size").And.NotContain("typed.Page");
        DescriptorSourceOf(runs[2], operationName).Should().NotContain("typed.Page").And.NotContain("typed.Size");
    }

    private static string DescriptorSourceOf(MultiGeneratorGenerationResult run, string operationName)
    {
        return run.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Single(source => source.HintName == $"TestNamespace.{operationName}_Descriptor.g.cs")
            .SourceText;
    }
}
