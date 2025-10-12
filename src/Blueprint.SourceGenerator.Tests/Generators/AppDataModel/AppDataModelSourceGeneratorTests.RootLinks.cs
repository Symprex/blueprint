namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorRootLinkTests
{
    [Fact]
    public async Task When_RootLink_on_operation_with_invalid_parameters_then_adds_Diagnostic_error()
    {
        // language=csharp
        var source = """
                     [RootLink("/test/{DoesNotExist}")]
                     public class RootLinkOperationWithNoParameters : IOperation {}
                     
                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_duplicate_RootLink_on_separate_operations_then_fails()
    {
        // language=csharp
        var source = """
                     [RootLink("/operations/a")]
                     public class OperationA : IOperation {}
                     
                     [RootLink("/operations/a")]
                     public class OperationB : IOperation {}

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }
}
