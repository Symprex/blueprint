using Blueprint.SourceGenerator.Generators.App;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public partial class AppDataModelSourceGeneratorTests
{
    public const string SnapshotsFolder = "Generators/App/Snapshots/";

    [Fact]
    public async Task When_not_partial_then_reports_diagnostic()
    {
        var source = """
                     [BlueprintAppScan]
                     public class TestBlueprintApp : BlueprintApp
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
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_class_implements_IOperation_then_included()
    {
        var source = """
                     public class IncludedOperation : IOperation {
                        public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
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
                     public class IncludedOperation : IOperation {
                        public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                     }
                     
                     public class AnotherIncludedOperation : IOperation {
                        public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_abstract_generic_base_with_concrete_subclass_then_only_concrete_is_registered()
    {
        var source = """
                     public abstract class OrderedPagedQuery<T> : IQuery<T>
                     {
                         public int PageSize { get; set; }
                         public int PageNumber { get; set; }
                     }

                     public partial class GetItemsQuery : OrderedPagedQuery<string>
                     {
                         public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_scan_attribute_has_error_type_then_reports_BP004()
    {
        var source = """
                     [BlueprintAppScan(FromAssemblyOf = typeof(NonExistentType))]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<AppDataModelSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
