using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

/// <summary>
/// Proves that the <see cref="OperationDocumentationFeatureData"/> emitted by the
/// <c>OperationDocumentationModule</c> at compile time is correctly populated on
/// descriptors at runtime, providing XML documentation fallback for OpenAPI rendering.
/// </summary>
public class OperationDocumentationFeatureDataTests
{
    [Fact]
    public void When_operation_has_xml_summary_then_descriptor_contains_documentation_feature_data()
    {
        // CreateLinkTestResourceCommand has an XML summary in LinkPopulationModuleIntegrationTests.cs
        var descriptor = CreateLinkTestResourceCommand.Configure();

        descriptor.TryGetFeatureData<OperationDocumentationFeatureData>(out var docData).Should().BeTrue();
        docData!.Summary.Should().NotBeNullOrWhiteSpace();
        docData.Summary.Should().Contain("creates a resource");
    }

    [Fact]
    public void When_operation_has_xml_summary_with_see_cref_then_tags_are_stripped()
    {
        // GetMappedLinkResourceQuery has an XML summary with mapped placeholder mention
        var descriptor = GetMappedLinkResourceQuery.Configure();

        descriptor.TryGetFeatureData<OperationDocumentationFeatureData>(out var docData).Should().BeTrue();
        docData!.Summary.Should().NotBeNullOrWhiteSpace();
        docData.Summary.Should().NotContain("<see");
    }

    [Fact]
    public void When_operation_without_xml_docs_then_descriptor_has_no_documentation_feature_data()
    {
        // CatchAllFolderApiResource's query has no XML docs
        var descriptor = GetCatchAllFolderResourceQuery.Configure();

        descriptor.TryGetFeatureData<OperationDocumentationFeatureData>(out var docData).Should().BeFalse();
    }
}
