using Blueprint.SourceGenerator.AppDataModel;
using Blueprint.SourceGenerator.OperationDescription;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTests
{
    public const string SnapshotsFolder = "Generators/OperationDescriptor/Snapshots/";

    [Fact]
    public async Task When_not_partial_then_reports_diagnostic()
    {
        var source = """
                     public class NotPartialOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_executed_then_includes_marker_attributes()
    {
        var source = """
                     public partial class IncludedOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder, includeMarkerAttributes: true);
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
