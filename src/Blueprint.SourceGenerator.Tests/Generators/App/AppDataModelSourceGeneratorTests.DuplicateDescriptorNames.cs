using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTests_DuplicateDescriptorNames
{
    [Fact]
    public async Task When_query_and_command_share_stripped_name_then_reports_BP009()
    {
        var source = """
                     public partial class FooQuery : IQuery<string>
                     {
                     }

                     public partial class FooCommand : ICommand
                     {
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public void When_query_and_unrelated_name_then_no_collision()
    {
        var source = """
                     public partial class FooQuery : IQuery<string>
                     {
                     }

                     public partial class BarCommand : ICommand
                     {
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BP009")
            .Should()
            .BeEmpty("different stripped names should not collide");
    }

    [Fact]
    public void When_query_and_background_task_share_prefix_then_no_collision()
    {
        var source = """
                     public partial class FooQuery : IQuery<string>
                     {
                     }

                     public partial class FooBackgroundTask : ICommand
                     {
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BP009")
            .Should()
            .BeEmpty("BackgroundTask suffix is not stripped, so Foo != FooBackgroundTask");
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<AppDataModelSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
