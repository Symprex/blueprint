namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsRootLinkTests
{
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
