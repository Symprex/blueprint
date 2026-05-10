using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.App;

public class TypedHttpClientExtensionsGeneratorTests
{
    public const string SnapshotsFolder = "Generators/App/Snapshots/";

    private const string Usings = """
                                  using System.ComponentModel.DataAnnotations;
                                  using Blueprint.Model;
                                  using Blueprint.Model.Attributes;
                                  using Blueprint.Runtime.Exec;

                                  namespace TestNamespace;

                                  """;

    [Fact]
    public async Task When_http_client_referenced_generates_extension_methods()
    {
        var source = """
                     public class GetTemplatesQuery : IQuery<string[]>
                     {
                         public int Page { get; set; }
                         public int PageSize { get; set; }
                         public static ApiOperationDescriptor Configure() => default!;
                     }

                     public class GetTemplateByIdQuery : IQuery<string>
                     {
                         [Required]
                         public string Id { get; set; }
                         public static ApiOperationDescriptor Configure() => default!;
                     }

                     public class CreateTemplateCommand : ICommand
                     {
                         [Required]
                         public string Name { get; set; }
                         public string? Description { get; set; }
                         public static ApiOperationDescriptor Configure() => default!;
                     }

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source).VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public void When_http_client_referenced_generates_non_empty_output()
    {
        var source = """
                     public class GetTemplatesQuery : IQuery<string[]>
                     {
                         public int Page { get; set; }
                         public static ApiOperationDescriptor Configure() => default!;
                     }

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);
        result.Output.Should().NotBeEmpty();
    }

    [Fact]
    public async Task When_operation_has_value_type_result_returns_non_nullable_task()
    {
        var source = """
                     public class GetItemQuery : IQuery<Results<OkResult<string>, NotFoundResult<string>>>
                     {
                         [Required]
                         public string Id { get; set; }
                         public static ApiOperationDescriptor Configure() => default!;
                     }

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                     }
                     """;

        await Compile(source).VerifySuccessAsync(SnapshotsFolder);
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<TypedHttpClientExtensionsGenerator>(Usings + source);
    }
}
