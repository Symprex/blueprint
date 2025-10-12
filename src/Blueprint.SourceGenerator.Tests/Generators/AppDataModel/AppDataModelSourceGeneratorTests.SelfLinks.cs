namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorSelfLinkTests
{
    [Fact]
    public async Task When_type_array_not_ILinkableResource_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps[]), "/operations/a")]
                     public class OperationA : IOperation {}

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_type_class_not_ILinkableResource_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(NonLinkableResource), "/operations/a")]
                     public class OperationA : IOperation {}

                     public class NonLinkableResource 
                     {
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_duplicate_no_param_SelfLink_on_separate_operations_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a")]
                     public class OperationA : IOperation {}
                     
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a")]
                     public class OperationB : IOperation {}
                     
                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }
                     
                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_param_not_found_on_operation_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a/{DoesNotExist}")]
                     public class OperationA : IOperation {}

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_mapped_param_not_found_on_operation_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithoutProps), "/operations/a/{Id:ResourcePropDoesNotExist}")]
                     public class OperationA : IOperation 
                     {
                         public required string Id { get; set; }
                     }

                     public class ApiResourceWithoutProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_duplicate_param_SelfLink_on_separate_operations_then_fails()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithProps), "/operations/a/{Id}")]
                     public class OperationA : IOperation 
                     {
                         public string Id { get; set; }
                     }
                     
                     [SelfLink(typeof(ApiResourceWithProps), "/operations/a/{ResourceId}")]
                     public class OperationB : IOperation
                     {
                         public string ResourceId { get; set; }
                     }
                     
                     [SelfLink(typeof(ApiResourceWithProps), "/operations/a/{Id:ResourceId}")]
                     public class OperationC : IOperation
                     {
                         public string Id { get; set; }
                     }
                     
                     public class ApiResourceWithProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                         
                         public string Id { get; set; } = null!;
                     
                         public string ResourceId { get; set; } = null!;
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_mapped_self_link_on_operation_then_url_registered_without_mapping()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(ApiResourceWithProps), "/operations/a/{Id:ResourceId}")]
                     public class OperationA : IOperation 
                     {
                         public string Id { get; set; }
                     }

                     public class ApiResourceWithProps : ApiResource 
                     {
                         // Not auto-generated because we are not running that generator
                         public override string Object => "resource";
                         
                         public string ResourceId { get; set; } = null!;
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel;
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }
}
