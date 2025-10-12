using Blueprint.SourceGenerator.AppDataModel;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTests
{
    public const string SnapshotsFolder = "Generators/AppDataModel/Snapshots/";

    [Fact]
    public async Task When_not_partial_then_reports_diagnostic()
    {
        var source = """
                     [BlueprintAppScan]
                     public class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_no_types_found_then_diagnostic_error()
    {
        var source = """
                     public class NotAnOperation {}

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_executed_then_includes_marker_attributes()
    {
        var source = """
                     public class IncludedOperation : IOperation {}
                     
                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder, includeMarkerAttributes: true);
    }

    [Fact]
    public async Task When_class_implements_IOperation_then_included()
    {
        var source = """
                     public class IncludedOperation : IOperation {}

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_multiple_classes_implement_IOperation_then_included()
    {
        var source = """
                     public class IncludedOperation : IOperation {}
                     public class AnotherIncludedOperation : IOperation {}

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintAppDataModel
                     {
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<AppDataModelSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
