using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorOperationGroupMetadataTests
{
    [Fact]
    public void When_http_descriptor_is_configured_then_operation_group_metadata_uses_the_operation_namespace_segment()
    {
        var data = HttpResponseMetadataQueryOp.Configure().GetFeatureData<OperationGroupMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.GroupName.Should().Be("OperationDescriptors");
    }

    [Fact]
    public void When_http_descriptor_declares_apioperations_group_then_operation_group_metadata_uses_the_explicit_group_name()
    {
        var data = ExplicitOperationGroupQueryOp.Configure().GetFeatureData<OperationGroupMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.GroupName.Should().Be("Billing");
    }

    [Fact]
    public void When_http_descriptor_declares_apioperations_with_description_then_group_description_is_projected()
    {
        var data = DescribedOperationGroupQueryOp.Configure().GetFeatureData<OperationGroupMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.GroupName.Should().Be("Invoices");
        data.GroupDescription.Should().Be("Invoice management and billing operations.");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.ApiOperations("Billing")]
[Blueprint.Model.Attributes.RootLink("/openapi/explicit-operation-group")]
public partial class ExplicitOperationGroupQueryOp : IQuery<string>
{
    public string Handle()
    {
        return "OK";
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.ApiOperations("Invoices", Description = "Invoice management and billing operations.")]
[Blueprint.Model.Attributes.RootLink("/openapi/described-operation-group")]
public partial class DescribedOperationGroupQueryOp : IQuery<string>
{
    public string Handle()
    {
        return "OK";
    }
}
