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

    [Fact]
    public void When_two_operations_share_a_name_only_once_it_is_cased_as_legacy_did_then_reports_BP009()
    {
        // Legacy ApiOperationDescriptor.cs:50 (S365 8b40ecc961) cased the name after removing "Query"/"Command", and its
        // StringExtensions.ToPascalCase lower-cases a capital not followed by a lower-case letter: "GetVCard" and
        // "GetvCard" are both "GetvCard".
        var source = """
                     public partial class GetVCardQuery : IQuery<string>
                     {
                     }

                     public partial class GetvCardCommand : ICommand
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
            .Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Should()
            .Equal(
                "Operations GetVCardQuery, GetvCardCommand produce the same descriptor name 'GetvCard' once 'Query' and "
                + "'Command' are removed and the name is cased. Rename one of them to avoid an OpenAPI operationId collision.");
    }

    [Fact]
    public void When_Query_occurs_inside_two_names_then_reports_BP009_on_the_names_legacy_gave_them()
    {
        // Legacy replaced "Query" anywhere, not only as a suffix (ApiOperationDescriptor.cs:50): "GetQueryResultsQuery"
        // and "GetResultsCommand" are both "GetResults".
        var source = """
                     public partial class GetQueryResultsQuery : IQuery<string>
                     {
                     }

                     public partial class GetResultsCommand : ICommand
                     {
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        Compile(source).Diagnostics.Where(static d => d.Id == "BP009").Should().ContainSingle();
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<AppDataModelSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
