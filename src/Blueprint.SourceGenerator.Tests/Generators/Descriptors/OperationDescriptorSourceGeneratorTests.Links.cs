namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsLinkTests
{
    [Fact]
    public async Task When_link_with_resource_type_and_rel_then_registers_link()
    {
        // language=csharp
        var source = """
                     [Link(typeof(ApiResourceWithProps), "/items/{Id}", Rel = "item")]
                     public partial class GetItemOperation : IOperation
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource
                     {
                         public override string Object => "item";
                         public string Id { get; set; } = null!;
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_missing_rel_then_infers_from_resource_type()
    {
        // language=csharp
        var source = """
                     [Link(typeof(ApiResourceWithProps), "/items/{Id}")]
                     public partial class GetItemOperation : IOperation
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource
                     {
                         public override string Object => "item";
                         public string Id { get; set; } = null!;
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_with_non_linkable_resource_then_reports_diagnostic()
    {
        // language=csharp
        var source = """
                     [Link(typeof(NonLinkableResource), "/items/{Id}", Rel = "item")]
                     public partial class GetItemOperation : IOperation
                     {
                         public required string Id { get; set; }
                     }

                     public class NonLinkableResource {}
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_placeholder_not_found_on_operation_then_reports_diagnostic()
    {
        // language=csharp
        var source = """
                     [Link(typeof(ApiResourceWithProps), "/items/{DoesNotExist}", Rel = "item")]
                     public partial class GetItemOperation : IOperation {}

                     public class ApiResourceWithProps : ApiResource
                     {
                         public override string Object => "item";
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_with_null_resource_type_then_registers_as_root_link()
    {
        // language=csharp
        var source = """
                     [Link(null, "/healthcheck")]
                     public partial class HealthCheckOperation : IOperation {}
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_combined_with_self_link_then_both_registered()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithProps), "/items/{Id}")]
                     [Link(typeof(OtherResource), "/items/{Id}/details", Rel = "details")]
                     public partial class GetItemOperation : IOperation
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource
                     {
                         public override string Object => "item";
                         public string Id { get; set; } = null!;
                     }

                     public class OtherResource : ApiResource
                     {
                         public override string Object => "other";
                         public string Id { get; set; } = null!;
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_link_with_mapped_placeholder_then_url_registered()
    {
        // language=csharp
        var source = """
                     [Link(typeof(ApiResourceWithProps), "/items/{Id:ResourceId}", Rel = "item")]
                     public partial class GetItemOperation : IOperation
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource
                     {
                         public override string Object => "item";
                         public string ResourceId { get; set; } = null!;
                     }
                     """;

        await OperationDescriptorSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(OperationDescriptorSourceGeneratorTests.SnapshotsFolder);
    }
}
