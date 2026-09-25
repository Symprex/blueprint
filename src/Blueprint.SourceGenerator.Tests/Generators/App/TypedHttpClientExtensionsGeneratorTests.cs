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

    [Fact]
    public async Task When_operation_has_property_named_operation_then_generated_client_compiles()
    {
        // Mirrors S365's CompleteAiChatCommand, whose `Operation` property is part of the wire payload and so cannot
        // be renamed. The flattened overload derives a parameter named `operation` from it, which the generated
        // method body must not shadow with its own local.
        var source = """
                     public class CompleteAiChatCommand : ICommand<string>
                     {
                         [Required]
                         public string Operation { get; set; } = default!;

                         [Required]
                         public string TenantId { get; set; } = default!;

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
    public async Task When_operation_has_properties_named_like_the_fixed_parameters_then_generated_client_compiles()
    {
        // `client` (the extension receiver) and `cancellationToken` are hard-coded parameter names, so properties
        // that camel-case to either would produce duplicate parameter names.
        var source = """
                     public class RegisterClientCommand : ICommand
                     {
                         [Required]
                         public string Client { get; set; } = default!;

                         public System.Threading.CancellationToken CancellationToken { get; set; }

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
    public async Task When_operation_is_shaped_like_S365_CompleteAiChatCommand_then_generated_client_compiles()
    {
        // Signature365 AiGateway's CompleteAiChatCommand, property for property (AiGateway.Contract/Api/CompleteAiChatCommand.cs):
        // its `Operation` property made the generated CompleteAiChatAsync declare `operation` twice (CS0136, CS0841), so
        // AiGateway did not build on v1.0.2.
        var source = """
                     public class AiChatMessage
                     {
                         public string Role { get; set; } = default!;
                         public string Content { get; set; } = default!;
                     }

                     public class CompleteAiChatResult
                     {
                         public string? Text { get; set; }
                     }

                     [Blueprint.Model.Attributes.HttpPost]
                     public class CompleteAiChatCommand : ICommand<CompleteAiChatResult>
                     {
                         [Required]
                         public string TenantId { get; set; } = default!;

                         [Required]
                         public string Feature { get; set; } = default!;

                         [Required]
                         public string Operation { get; set; } = default!;

                         [Required]
                         public string IdempotencyKey { get; set; } = default!;

                         [Required]
                         public System.Collections.Generic.List<AiChatMessage> Messages { get; set; } = [];

                         public string? ModelHint { get; set; }

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
    public async Task When_operation_has_properties_named_like_every_generator_owned_name_then_generated_client_compiles()
    {
        // Every name the flattened overload owns is taken, some twice over, so each must step past the numbered form too;
        // `Options` is a parameter of the pre-built overloads only, beside no property-derived name.
        var source = """
                     public class ConfigureClientCommand : ICommand
                     {
                         [Required]
                         public string Client { get; set; } = default!;

                         public string? Client1 { get; set; }

                         public System.Threading.CancellationToken CancellationToken { get; set; }

                         [Required]
                         public string Operation { get; set; } = default!;

                         public string? Operation1 { get; set; }

                         public string? Options { get; set; }

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
