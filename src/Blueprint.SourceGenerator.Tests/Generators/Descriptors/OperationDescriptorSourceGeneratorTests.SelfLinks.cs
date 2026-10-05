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

    [Fact]
    public async Task When_mapped_self_link_placeholder_name_has_an_underscore_then_url_registered_without_mapping()
    {
        // R120: legacy's placeholder parser (Signature365 8b40ecc961, Blueprint/ApiOperationLink.cs:29,
        // propName = ".*?") accepted any property name before ':' or '(', underscores included. The generator's own
        // mapped-placeholder pattern restricted the name to [A-Za-z0-9]+, so a placeholder whose property name had an
        // underscore was never matched at all and its mapping was left in the registered URL, unstripped.
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithUnderscoreProp), "/operations/a/{User_Id:ResourceId}")]
                     public partial class OperationA : IOperation
                     {
                         public string User_Id { get; set; }
                     }

                     public class ApiResourceWithUnderscoreProp : ApiResource
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
