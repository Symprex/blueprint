namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsSelfLinkTests
{
    [Fact]
    public async Task When_type_array_not_ILinkableResource_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps[]), "/operations/a")]
                     public partial class OperationA : IOperation {}

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_type_class_not_ILinkableResource_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(NonLinkableResource), "/operations/a")]
                     public partial class OperationA : IOperation {}

                     public class NonLinkableResource 
                     {
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_param_not_found_on_operation_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a/{DoesNotExist}")]
                     public partial class OperationA : IOperation {}

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_mapped_param_not_found_on_operation_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a/{Id:ResourcePropDoesNotExist}")]
                     public partial class OperationA : IOperation 
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_unmapped_param_not_found_on_resource_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a/{Id}")]
                     public partial class OperationA : IOperation 
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_mapped_self_link_on_operation_then_url_registered_without_mapping()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithProps), "/operations/a/{Id:ResourceId}")]
                     public partial class OperationA : IOperation 
                     {
                         public string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                         
                         public string ResourceId { get; set; } = null!;
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }
}
