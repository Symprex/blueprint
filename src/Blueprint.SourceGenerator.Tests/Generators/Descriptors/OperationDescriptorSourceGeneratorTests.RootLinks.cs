using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsRootLinkTests
{
    private const string RootLinksFeatureData =
        "this.SetFeatureData(new global::Blueprint.Model.OperationRootLinksFeatureData(global::System.Collections.Immutable.ImmutableArray.Create(";

    [Fact]
    public void When_RootLink_has_no_Rel_then_descriptor_records_it_under_the_name_legacy_gave_the_operation()
    {
        // Legacy's rel defaulted to the descriptor name when the link was built (Signature365 8b40ecc961,
        // Blueprint/Configuration/OperationScanner.cs:294-298), and that name was cased (ApiOperationDescriptor.cs:50).
        // language=csharp
        var source = """
                     [RootLink("/vcards/{Id}")]
                     public partial class GenerateVCardQuery : IOperation
                     {
                         public string Id { get; set; }
                     }
                     """;

        DescriptorSource(source, "GenerateVCardQuery").Should().Contain(
            RootLinksFeatureData + "new global::Blueprint.Model.OperationRootLink(\"GeneratevCard\", \"/vcards/{Id}\"))));");
    }

    [Fact]
    public void When_RootLink_has_a_Rel_then_descriptor_records_it_under_that_Rel()
    {
        // OperationScanner.cs:297: linkAttribute.Rel ?? descriptor.Name.
        // language=csharp
        var source = """
                     [RootLink("/messages/bulk-delete", Rel = "bulk-delete")]
                     public partial class BulkDeleteMessagesCommand : IOperation {}
                     """;

        DescriptorSource(source, "BulkDeleteMessagesCommand").Should().Contain(
            RootLinksFeatureData + "new global::Blueprint.Model.OperationRootLink(\"bulk-delete\", \"/messages/bulk-delete\"))));");
    }

    [Fact]
    public void When_Link_has_no_resource_type_then_descriptor_records_it_as_a_root_link()
    {
        // A link without a resource type was a root link (ApiDataModel.cs:212-215; ApiOperationLink.cs:176).
        // language=csharp
        var source = """
                     [Link(null, "/reports")]
                     public partial class ListReportsQuery : IOperation {}
                     """;

        DescriptorSource(source, "ListReportsQuery").Should().Contain(
            RootLinksFeatureData + "new global::Blueprint.Model.OperationRootLink(\"ListReports\", \"/reports\"))));");
    }

    [Fact]
    public void When_operation_has_several_RootLinks_then_descriptor_records_each_under_its_own_Rel()
    {
        // Legacy's RootLinkAttribute allowed several (Signature365 8b40ecc961, Blueprint/RootLinkAttribute.cs:5,
        // AllowMultiple = true), and its scanner built a link from each, with that attribute's own Rel
        // (Blueprint/Configuration/OperationScanner.cs:294-298). The Rel is on the second, so pairing each URL with the
        // first attribute's Rel would key both by the operation's name.
        // language=csharp
        var source = """
                     [RootLink("/reports")]
                     [RootLink("/reports/recent", Rel = "recent-reports")]
                     public partial class ListReportsQuery : IOperation {}
                     """;

        var descriptor = DescriptorSource(source, "ListReportsQuery");

        descriptor.Should().Contain("this.AddUrl(new global::Blueprint.Model.ApiOperationUrl(\"/reports\"));")
            .And.Contain("this.AddUrl(new global::Blueprint.Model.ApiOperationUrl(\"/reports/recent\"));")
            .And.Contain(RootLinksFeatureData
                         + "new global::Blueprint.Model.OperationRootLink(\"ListReports\", \"/reports\"), "
                         + "new global::Blueprint.Model.OperationRootLink(\"recent-reports\", \"/reports/recent\"))));");
    }

    [Fact]
    public void When_operation_has_no_root_link_then_descriptor_records_none()
    {
        // language=csharp
        var source = """
                     public partial class NoRootLinkQuery : IOperation {}
                     """;

        DescriptorSource(source, "NoRootLinkQuery").Should().NotContain("OperationRootLinksFeatureData");
    }

    private static string DescriptorSource(string source, string typeName)
    {
        return OperationDescriptorSourceGeneratorTests.Compile(source).Output
            .Single(tree => tree.FilePath.EndsWith(typeName + "_Descriptor.g.cs", StringComparison.Ordinal))
            .ToString();
    }

    [Fact]
    public async Task When_RootLink_on_operation_with_invalid_parameters_then_adds_Diagnostic_error()
    {
        // language=csharp
        var source = """
                     [RootLink("/test/{DoesNotExist}")]
                     public partial class RootLinkOperationWithNoParameters : IOperation {}
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }
}
