using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class ValidationModuleTests
{
    private const string DescriptorSnapshotsFolder = "Generators/OperationDescriptor/Snapshots/";
    private const string PipelineSnapshotsFolder = "Generators/Pipelines/Snapshots/Integration/";

    [Fact]
    public void When_operation_has_validation_attributes_then_validation_module_contributes_validation_stage()
    {
        var compilation = CreateCompilation(
            """
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class IncludedOperation : IOperation
            {
                [Required]
                public string? Name { get; set; }
            }
            """);
        var config = new BlueprintSourceGenerationConfig([new ValidationModule()]);

        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Validation, nameof(ValidationModule)));
        result.Value.RenderSteps.Should().ContainSingle(static step =>
            step.Stage == PipelineStage.Validation && step.ContributorId == nameof(ValidationModule));
    }

    [Fact]
    public void When_operation_implements_IValidatableObject_then_validation_module_contributes_validation_stage()
    {
        var compilation = CreateCompilation(
            """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class IncludedOperation : IOperation, IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """);
        var config = new BlueprintSourceGenerationConfig([new ValidationModule()]);

        var result = config.BuildPipelinePlan("TestNamespace.IncludedOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().ContainSingle().Which.Should().Be(
            new PipelineContribution(PipelineStage.Validation, nameof(ValidationModule)));
        result.Value.RenderSteps.Should().ContainSingle(static step =>
            step.Stage == PipelineStage.Validation && step.ContributorId == nameof(ValidationModule));
    }

    [Fact]
    public async Task When_compare_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Compare("MissingProperty")]
                         public string? Password { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV001");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public void When_compare_attribute_references_existing_property_then_validation_diagnostic_is_not_reported()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         public string? Confirmation { get; set; }

                         [Compare(nameof(Confirmation))]
                         public string? Password { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Inline_handler_with_required_and_min_length_validation_attributes_generates_fast_property_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         [MinLength(3)]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Name)");
        pipelineSource.Should().Contain("operation.Name.Length < 3");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.MinLengthAttribute(3).FormatErrorMessage(\"Name\")");
        pipelineSource.Should().Contain("throw new global::Blueprint.Validation.ValidationException(validationResults);");
        pipelineSource.IndexOf("global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_string_length_validation_attributes_generates_fast_property_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [StringLength(5, MinimumLength = 3)]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Name.Length < 3");
        pipelineSource.Should().Contain("operation.Name.Length > 5");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.StringLengthAttribute(5)");
        pipelineSource.Should().Contain("MinimumLength = 3");
        pipelineSource.IndexOf("operation.Name.Length < 3", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_email_address_validation_attributes_generates_fast_property_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [EmailAddress]
                         public string? Email { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Email ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.IsValidEmailAddress(operation.Email) == false");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.EmailAddressAttribute().FormatErrorMessage(\"Email\")");
        pipelineSource.IndexOf("global::Blueprint.Validation.ValidationRuntime.IsValidEmailAddress(operation.Email) == false", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_required_only_validation_attributes_generates_fast_required_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         public string? Name { get; set; }

                         [Required]
                         public int? Age { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Name}:{command.Age}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Name)");
        pipelineSource.Should().Contain("operation.Age is null");
        pipelineSource.IndexOf("global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_required_and_max_length_validation_attributes_generates_fast_property_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         [MaxLength(3)]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Name)");
        pipelineSource.Should().Contain("operation.Name.Length > 3");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(3).FormatErrorMessage(\"Name\")");
        var requiredIndex = pipelineSource.IndexOf("global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal);
        var maxLengthIndex = pipelineSource.IndexOf("operation.Name.Length > 3", StringComparison.Ordinal);
        var handlerIndex = pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal);
        requiredIndex.Should().BeLessThan(maxLengthIndex);
        maxLengthIndex.Should().BeLessThan(handlerIndex);
    }

    [Fact]
    public void Inline_handler_with_required_allow_empty_strings_uses_runtime_property_validation_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required(AllowEmptyStrings = true)]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.ValidateProperty");
        pipelineSource.Should().Contain("context.Descriptor.GetPropertyAttributes(\"Name\")");
    }

    [Fact]
    public void Inline_handler_with_display_named_required_attribute_uses_runtime_property_validation_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Display(Name = "Display name")]
                         [Required]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.ValidateProperty");
        pipelineSource.Should().Contain("context.Descriptor.GetPropertyAttributes(\"Name\")");
        pipelineSource.Should().NotContain("global::System.String.IsNullOrWhiteSpace(operation.Name)");
    }

    [Fact]
    public void Inline_handler_with_IValidatableObject_uses_data_annotations_validation_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.Collections.Generic;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>, IValidatableObject
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         public string? Name { get; set; }

                         public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                         {
                             yield break;
                         }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
    }

    [Fact]
    public async Task Inline_handler_with_validation_attributes_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public async Task Inline_handler_with_required_and_max_length_validation_attributes_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         [MaxLength(3)]
                         public string? Name { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Name ?? string.Empty);
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_mixed_fast_and_runtime_validation_properties_generates_fast_and_runtime_checks_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         [MaxLength(3)]
                         public string? Name { get; set; }

                         [Required(ErrorMessage = "Please provide a code.")]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Name}:{command.Code}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("global::System.String.IsNullOrWhiteSpace(operation.Name)");
        pipelineSource.Should().Contain("operation.Name.Length > 3");
        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.ValidateProperty");
        pipelineSource.Should().Contain("context.Descriptor.GetPropertyAttributes(\"Code\")");
        pipelineSource.IndexOf("global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("global::Blueprint.Validation.ValidationRuntime.ValidateProperty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_mixed_fast_and_runtime_validation_properties_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using System.ComponentModel.DataAnnotations;
                     using System.Threading.Tasks;

                     namespace TestNamespace;

                     [BlueprintAppScan]
                     public partial class TestApp : BlueprintApp
                     {
                         public TestApp()
                         {
                             this.Register(CreateUserCommand.Configure());
                         }
                     }

                     public class CreateUserCommand : ICommand<string>
                     {
                         public static ApiOperationDescriptor Configure()
                         {
                             return new ApiOperationDescriptor(typeof(CreateUserCommand), nameof(CreateUserCommand), "Tests");
                         }

                         [Required]
                         [MaxLength(3)]
                         public string? Name { get; set; }

                         [Required(ErrorMessage = "Please provide a code.")]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Name}:{command.Code}");
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }

    private static GenerationResult CompilePipeline(string source)
    {
        return TestHelpers.Compile<OperationExecutorPipelineSourceGenerator>(source, assertCacheability: false);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "ValidationModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
