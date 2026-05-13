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
    public async Task When_greater_than_property_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [GreaterThanProperty("MissingProperty")]
                         public decimal Maximum { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV002");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public async Task When_required_if_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [RequiredIf("MissingProperty", true)]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV003");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public async Task When_required_if_not_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [RequiredIfNot("MissingProperty", null)]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV004");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public async Task When_not_allowed_if_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [NotAllowedIf("MissingProperty", true)]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV005");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_greater_than_property_validation_attribute_generates_property_comparison_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public decimal Minimum { get; set; }

                         [GreaterThanProperty(nameof(Minimum))]
                         public decimal Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Minimum}:{command.Maximum}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Maximum <= operation.Minimum");
        pipelineSource.Should().Contain("global::Blueprint.Validation.GreaterThanPropertyAttribute");
        pipelineSource.IndexOf("operation.Maximum <= operation.Minimum", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_greater_than_property_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public decimal Minimum { get; set; }

                         [GreaterThanProperty(nameof(Minimum))]
                         public decimal Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Minimum}:{command.Maximum}");
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_required_if_validation_attribute_generates_conditional_required_check_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public bool Enabled { get; set; }

                         [RequiredIf(nameof(Enabled), true)]
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
        pipelineSource.Should().Contain("operation.Enabled == true && global::System.String.IsNullOrWhiteSpace(operation.Name)");
        pipelineSource.Should().Contain("global::Blueprint.Validation.RequiredIfAttribute");
        pipelineSource.IndexOf("operation.Enabled == true && global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_required_if_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public bool Enabled { get; set; }

                         [RequiredIf(nameof(Enabled), true)]
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
    public void Inline_handler_with_required_if_not_validation_attribute_generates_conditional_required_check_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public string? TokenId { get; set; }

                         [RequiredIfNot(nameof(TokenId), null)]
                         public string? PaymentIntentId { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.PaymentIntentId ?? string.Empty);
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.TokenId is not null && global::System.String.IsNullOrWhiteSpace(operation.PaymentIntentId)");
        pipelineSource.Should().Contain("global::Blueprint.Validation.RequiredIfNotAttribute");
        pipelineSource.IndexOf("operation.TokenId is not null && global::System.String.IsNullOrWhiteSpace(operation.PaymentIntentId)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_required_if_not_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public string? TokenId { get; set; }

                         [RequiredIfNot(nameof(TokenId), null)]
                         public string? PaymentIntentId { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.PaymentIntentId ?? string.Empty);
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_not_allowed_if_validation_attribute_generates_conditional_forbidden_check_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public bool Enabled { get; set; }

                         [NotAllowedIf(nameof(Enabled), true)]
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
        pipelineSource.Should().Contain("operation.Enabled == true && operation.Name is not null");
        pipelineSource.Should().Contain("global::Blueprint.Validation.NotAllowedIfAttribute");
        pipelineSource.IndexOf("operation.Enabled == true && operation.Name is not null", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_not_allowed_if_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public bool Enabled { get; set; }

                         [NotAllowedIf(nameof(Enabled), true)]
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
    public void Inline_handler_with_not_allowed_if_null_dependent_value_uses_runtime_fallback()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public string? TokenId { get; set; }

                         [NotAllowedIf(nameof(TokenId), null)]
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

        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.ValidateProperty");
        pipelineSource.Should().NotContain("operation.TokenId is null && operation.Name is not null");
    }

    [Fact]
    public void Inline_handler_with_greater_than_validation_attribute_generates_literal_comparison_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [GreaterThan(10)]
                         public int Count { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Count.ToString());
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Count <= 10");
        pipelineSource.Should().Contain("global::Blueprint.Validation.GreaterThanAttribute");
        pipelineSource.IndexOf("operation.Count <= 10", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_greater_than_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [GreaterThan(10)]
                         public int Count { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Count.ToString());
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_unsigned_greater_than_validation_attribute_generates_literal_comparison_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [GreaterThan(10)]
                         public uint Count { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Count.ToString());
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Count <= 10");
        pipelineSource.Should().Contain("global::Blueprint.Validation.GreaterThanAttribute");
        pipelineSource.IndexOf("operation.Count <= 10", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_less_than_validation_attribute_generates_literal_comparison_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [LessThan(100)]
                         public int Count { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Count.ToString());
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Count >= 100");
        pipelineSource.Should().Contain("global::Blueprint.Validation.LessThanAttribute");
        pipelineSource.IndexOf("operation.Count >= 100", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_less_than_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [LessThan(100)]
                         public int Count { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Count.ToString());
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public async Task When_less_than_property_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [LessThanProperty("MissingProperty")]
                         public decimal End { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV006");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_less_than_property_validation_attribute_generates_property_comparison_before_handler_invocation()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [LessThanProperty(nameof(Maximum))]
                         public decimal Minimum { get; set; }

                         public decimal Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Minimum}:{command.Maximum}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Minimum >= operation.Maximum");
        pipelineSource.Should().Contain("global::Blueprint.Validation.LessThanPropertyAttribute");
        pipelineSource.IndexOf("operation.Minimum >= operation.Maximum", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_less_than_property_validation_attribute_generates_correctly()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [LessThanProperty(nameof(Maximum))]
                         public decimal Minimum { get; set; }

                         public decimal Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Minimum}:{command.Maximum}");
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void When_greater_than_property_attribute_on_nullable_properties_then_generates_guarded_comparison()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         public int? Minimum { get; set; }

                         [GreaterThanProperty(nameof(Minimum))]
                         public int? Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Minimum}:{command.Maximum}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Maximum is not null && operation.Minimum is not null");
        pipelineSource.Should().Contain("operation.Maximum.Value <= operation.Minimum.Value");
    }

    [Fact]
    public void When_less_than_property_attribute_on_nullable_properties_then_generates_guarded_comparison()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
                     using System;
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

                         [LessThanProperty(nameof(Maximum))]
                         public DateTime? Start { get; set; }

                         public DateTime? Maximum { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult($"{command.Start}:{command.Maximum}");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Start is not null && operation.Maximum is not null");
        pipelineSource.Should().Contain("operation.Start.Value >= operation.Maximum.Value");
    }

    [Fact]
    public void When_compare_attribute_on_properties_then_generates_fast_path()
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

                         public string? Password { get; set; }

                         [Compare(nameof(Password))]
                         public string? ConfirmPassword { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult("ok");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("!object.Equals(operation.ConfirmPassword, operation.Password)");
        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.CompareAttribute");
        pipelineSource.IndexOf("!object.Equals(operation.ConfirmPassword, operation.Password)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_compare_validation_attribute_generates_correctly()
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

                         public string? Password { get; set; }

                         [Compare(nameof(Password))]
                         public string? ConfirmPassword { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult("ok");
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
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
        pipelineSource.Should().Contain("_va_Name.FormatErrorMessage(\"Name\")");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.MinLengthAttribute(3)");
        pipelineSource.Should().Contain("return Blueprint.Runtime.Exec.PipelineHelpers.CreateValidationFailure(context, validationResults);");
        pipelineSource.Should().NotContain("throw new global::Blueprint.Validation.ValidationException(validationResults);");

        // Validation exception handling now lives in OperationExecutorShell, not in generated validation code
        pipelineSource.Should().NotContain("catch (Blueprint.Validation.ValidationException");
        pipelineSource.Should().NotContain("catch (System.ComponentModel.DataAnnotations.ValidationException");

        pipelineSource.IndexOf("global::System.String.IsNullOrWhiteSpace(operation.Name)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void Inline_handler_with_required_and_min_length_validation_attributes_uses_canonical_validation_failed_result_name()
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

        pipelineSource.Should().Contain("return Blueprint.Runtime.Exec.PipelineHelpers.CreateValidationFailure(context, validationResults);");
        pipelineSource.Should().NotContain("return new Blueprint.Middleware.ValidationFailedOperationResult(validationResults);");

        // Validation exception translation catch blocks are now in OperationExecutorShell at runtime
        pipelineSource.Should().NotContain("catch (Blueprint.Validation.ValidationException");
        pipelineSource.Should().NotContain("catch (System.ComponentModel.DataAnnotations.ValidationException");
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
        pipelineSource.Should().Contain("(_va_Name).FormatErrorMessage(\"Name\")");
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
        pipelineSource.Should().Contain("_va_Email.FormatErrorMessage(\"Email\")");
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
        pipelineSource.Should().Contain("_va_Name.FormatErrorMessage(\"Name\")");
        pipelineSource.Should().Contain("_va_Name_1.FormatErrorMessage(\"Name\")");
        pipelineSource.Should().Contain("new global::System.ComponentModel.DataAnnotations.MaxLengthAttribute(3)");
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

        pipelineSource.Should().Contain("((global::System.ComponentModel.DataAnnotations.IValidatableObject)",
            "IValidatableObject should be called directly instead of TryValidateObject fallback");
        pipelineSource.Should().Contain("ivoFailures");
        pipelineSource.Should().Contain("return Blueprint.Runtime.Exec.PipelineHelpers.CreateValidationFailure(context, ivoFailures);");
        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject",
            "IValidatableObject-only types should use direct Validate() call, not whole-object TryValidateObject fallback");
        pipelineSource.Should().NotContain("throw new global::Blueprint.Validation.ValidationException(validationResults);");
    }

    [Fact]
    public void Inline_handler_with_IValidatableObject_and_property_attributes_uses_hybrid_validation()
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

                         [Required]
                         public string? Name { get; set; }

                         [Range(1, 100)]
                         public int Age { get; set; }

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

        // Property-level checks should be generated (fast-path)
        pipelineSource.Should().Contain("operation.Name",
            "property-level [Required] check should be generated even when IValidatableObject is implemented");
        pipelineSource.Should().Contain("operation.Age",
            "property-level [Range] check should be generated even when IValidatableObject is implemented");

        // IValidatableObject.Validate() should be called as a tail call after property checks
        pipelineSource.Should().Contain("((global::System.ComponentModel.DataAnnotations.IValidatableObject)",
            "IValidatableObject.Validate() should be called after generated property checks");
        pipelineSource.Should().Contain("ivoFailures");

        // Should NOT use the whole-object TryValidateObject fallback
        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject",
            "hybrid mode should not use TryValidateObject — property checks are generated and IValidatableObject is called directly");
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

    [Fact]
    public void Inline_handler_with_range_validation_attribute_generates_bound_check_before_handler_invocation()
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

                         [Range(1, 100)]
                         public int Priority { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Priority.ToString());
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Priority < 1 || operation.Priority > 100");
        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.RangeAttribute");
        pipelineSource.IndexOf("operation.Priority < 1 || operation.Priority > 100", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_range_validation_attribute_generates_correctly()
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

                         [Range(1, 100)]
                         public int Priority { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Priority.ToString());
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void Inline_handler_with_regex_validation_attribute_generates_regex_check_before_handler_invocation()
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

                         [RegularExpression(@"^[A-Z]{2,5}$")]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Code ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain(".IsMatch(operation.Code)");
        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute");
        pipelineSource.Should().Contain("RegexOptions.Compiled", "regex pattern should be compiled for fast matching");
        pipelineSource.IndexOf(".IsMatch(operation.Code)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_regex_validation_attribute_generates_correctly()
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

                         [RegularExpression(@"^[A-Z]{2,5}$")]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Code ?? "");
                         }
                     }
                     """;

        await CompilePipeline(source)
            .VerifySuccessAsync(PipelineSnapshotsFolder);
    }

    [Fact]
    public void When_range_attribute_uses_double_overload_then_generates_bound_check_before_handler_invocation()
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

                         [Range(0.5, 99.9)]
                         public double Score { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Score.ToString());
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Score < 0.5 || operation.Score > 99.9");
        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.RangeAttribute");
        pipelineSource.IndexOf("operation.Score < 0.5 || operation.Score > 99.9", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public void When_range_attribute_on_nullable_int_then_generates_guarded_bound_check()
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

                         [Range(1, 100)]
                         public int? Priority { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Priority?.ToString() ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Priority is not null");
        pipelineSource.Should().Contain("operation.Priority.Value < 1 || operation.Priority.Value > 100");
        pipelineSource.Should().Contain("global::System.ComponentModel.DataAnnotations.RangeAttribute");
    }

    [Fact]
    public void When_greater_than_attribute_on_nullable_int_then_generates_guarded_comparison()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [GreaterThan(0)]
                         public int? Score { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Score?.ToString() ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Score is not null");
        pipelineSource.Should().Contain("operation.Score.Value <= 0");
        pipelineSource.Should().Contain("global::Blueprint.Validation.GreaterThanAttribute");
    }

    [Fact]
    public void When_less_than_attribute_on_nullable_int_then_generates_guarded_comparison()
    {
        var source = """
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [LessThan(1000)]
                         public int? Limit { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Limit?.ToString() ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.Limit is not null");
        pipelineSource.Should().Contain("operation.Limit.Value >= 1000");
        pipelineSource.Should().Contain("global::Blueprint.Validation.LessThanAttribute");
    }

    [Fact]
    public void When_regex_attribute_has_named_arguments_then_falls_back_to_runtime_validation()
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

                         [RegularExpression(@"^[A-Z]+$", ErrorMessage = "Must be uppercase letters")]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Code ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        // ErrorMessage named argument forces per-property runtime fallback.
        pipelineSource.Should().Contain("global::Blueprint.Validation.ValidationRuntime.ValidateProperty");
    }

    [Fact]
    public void Inline_handler_with_phone_validation_attribute_generates_phone_check_before_handler_invocation()
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

                         [Phone]
                         public string? PhoneNumber { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.PhoneNumber ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("_va_PhoneNumber.IsValid(operation.PhoneNumber) == false");
        pipelineSource.Should().Contain("operation.PhoneNumber is not null");
    }

    [Fact]
    public void Inline_handler_with_url_validation_attribute_generates_url_check_before_handler_invocation()
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

                         [Url]
                         public string? Website { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Website ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("_va_Website.IsValid(operation.Website) == false");
        pipelineSource.Should().Contain("operation.Website is not null");
    }

    [Fact]
    public void Inline_handler_with_allowed_values_string_attribute_generates_equality_checks_before_handler_invocation()
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

                         [AllowedValues("admin", "user", "guest")]
                         public string? Role { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Role ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("operation.Role != \"admin\" && operation.Role != \"user\" && operation.Role != \"guest\"");
        pipelineSource.Should().Contain("operation.Role is not null");
        pipelineSource.Should().Contain("AllowedValuesAttribute");
        pipelineSource.Should().NotContain("Validator.TryValidateObject");
    }

    [Fact]
    public void Inline_handler_with_denied_values_string_attribute_generates_equality_checks_before_handler_invocation()
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

                         [DeniedValues("root", "system")]
                         public string? Username { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Username ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("operation.Username == \"root\" || operation.Username == \"system\"");
        pipelineSource.Should().Contain("operation.Username is not null");
        pipelineSource.Should().Contain("DeniedValuesAttribute");
        pipelineSource.Should().NotContain("Validator.TryValidateObject");
    }

    [Fact]
    public void Inline_handler_with_length_attribute_generates_length_check_before_handler_invocation()
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

                         [Length(2, 50)]
                         public string? Code { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult(command.Code ?? "");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().Contain("operation.Code.Length < 2 || operation.Code.Length > 50");
        pipelineSource.Should().Contain("operation.Code is not null");
        pipelineSource.Should().Contain("LengthAttribute(2, 50)");
        pipelineSource.Should().NotContain("Validator.TryValidateObject");
    }

    [Fact]
    public async Task When_must_be_greater_than_or_equal_attribute_references_missing_property_then_reports_validation_diagnostic()
    {
        var source = """
                     using Blueprint.Validation;

                     public partial class IncludedOperation : IOperation
                     {
                         [MustBeGreaterThanOrEqual("MissingProperty")]
                         public decimal Maximum { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV007");
        await result.VerifyCompilationFailedAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public void When_MinLength_exceeds_MaxLength_then_reports_BPV008()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [MinLength(10)]
                         [MaxLength(5)]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV008");
    }

    [Fact]
    public void When_MinLength_does_not_exceed_MaxLength_then_no_BPV008()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [MinLength(3)]
                         [MaxLength(50)]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV008").Should().BeEmpty();
    }

    [Fact]
    public void When_Range_minimum_exceeds_maximum_then_reports_BPV009()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Range(100, 10)]
                         public int Priority { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV009");
    }

    [Fact]
    public void When_Range_minimum_does_not_exceed_maximum_then_no_BPV009()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Range(1, 100)]
                         public int Priority { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV009").Should().BeEmpty();
    }

    [Fact]
    public void When_Range_double_minimum_exceeds_maximum_then_reports_BPV009()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Range(99.9, 1.5)]
                         public double Score { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV009");
    }

    [Fact]
    public void When_StringLength_MinimumLength_exceeds_maximum_then_reports_BPV010()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [StringLength(5, MinimumLength = 10)]
                         public string? Code { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV010");
    }

    [Fact]
    public void When_StringLength_MinimumLength_does_not_exceed_maximum_then_no_BPV010()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [StringLength(50, MinimumLength = 3)]
                         public string? Code { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV010").Should().BeEmpty();
    }

    [Fact]
    public void When_RegularExpression_has_empty_pattern_then_reports_BPV011()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [RegularExpression("")]
                         public string? Code { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV011");
    }

    [Fact]
    public void When_RegularExpression_has_non_empty_pattern_then_no_BPV011()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [RegularExpression(@"^[A-Z]+$")]
                         public string? Code { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV011").Should().BeEmpty();
    }

    [Fact]
    public void When_Required_on_non_nullable_value_type_then_reports_BPV012()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public int Count { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV012");
    }

    [Fact]
    public void When_Required_on_nullable_value_type_then_no_BPV012()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public int? Count { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV012").Should().BeEmpty();
    }

    [Fact]
    public void When_Required_on_reference_type_then_no_BPV012()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV012").Should().BeEmpty();
    }

    [Fact]
    public void When_Required_on_non_nullable_Guid_then_reports_BPV012()
    {
        var source = """
                     using System;
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public Guid Id { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV012");
    }

    [Fact]
    public void When_operation_implements_IValidatableObject_then_reports_BPV013()
    {
        var source = """
                     using System.Collections.Generic;
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation, IValidatableObject
                     {
                         public string? Name { get; set; }

                         public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                         {
                             yield break;
                         }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV013");
    }

    [Fact]
    public void When_operation_does_not_implement_IValidatableObject_then_no_BPV013()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV013").Should().BeEmpty();
    }

    [Fact]
    public void When_Required_and_DefaultValue_on_same_property_then_reports_BPV014()
    {
        var source = """
                     using System.ComponentModel;
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         [DefaultValue(0)]
                         public int? Count { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPV014");
    }

    [Fact]
    public void When_Required_without_DefaultValue_then_no_BPV014()
    {
        var source = """
                     using System.ComponentModel.DataAnnotations;

                     public partial class IncludedOperation : IOperation
                     {
                         [Required]
                         public string? Name { get; set; }
                     }
                     """;

        var result = CompileDescriptor(source);

        result.Diagnostics.Where(static d => d.Id == "BPV014").Should().BeEmpty();
    }

    [Fact]
    public void Inline_handler_with_additional_blueprint_validation_attributes_generates_fast_property_checks_before_handler_invocation()
    {
        var source = """
                     using System;
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [MustBeTrue("Accept the terms.")]
                         public bool AcceptedTerms { get; set; }

                         [InFuture("Future only.")]
                         public DateTime? StartsAt { get; set; }

                         [InPast]
                         public DateTimeOffset? CompletedAt { get; set; }

                         [NotInFuture]
                         public DateTime PublishedAt { get; set; }

                         [NotInPast]
                         public DateTimeOffset ScheduledAt { get; set; }

                         [NotEmptyList("Add at least one item.")]
                         public int[]? ItemIds { get; set; }

                         [MaxItemsList(2, "Too many items.")]
                         public int[]? ScoreIds { get; set; }

                         [GreaterThanOrEqualTo(5, "Minimum five.")]
                         public decimal? MinimumAge { get; set; }

                         [LessThanOrEqualTo(10)]
                         public float? MaximumAge { get; set; }

                         public int? MinimumValue { get; set; }

                         [MustBeGreaterThanOrEqual(nameof(MinimumValue))]
                         public int? MaximumValue { get; set; }

                         [Password("Weak password.")]
                         public string? Password { get; set; }

                         [ResourceKey("Bad resource key.")]
                         public string? ResourceKey { get; set; }

                         [UkPostcode("Bad postcode.")]
                         public string? Postcode { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult("ok");
                         }
                     }
                     """;

        var result = CompilePipeline(source);

        result.Diagnostics.Should().BeEmpty();
        var pipelineSource = result.Output.Single(static tree => tree.FilePath.EndsWith("CreateUserCommandExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();

        pipelineSource.Should().NotContain("global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject");
        pipelineSource.Should().Contain("operation.AcceptedTerms == false");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.MustBeTrueAttribute(\"Accept the terms.\")");
        pipelineSource.Should().Contain("operation.StartsAt is not null");
        pipelineSource.Should().Contain("operation.StartsAt.Value <= global::System.DateTime.UtcNow");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.InFutureAttribute(\"Future only.\")");
        pipelineSource.Should().Contain("operation.CompletedAt is not null");
        pipelineSource.Should().Contain("operation.CompletedAt.Value >= global::System.DateTimeOffset.UtcNow");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.InPastAttribute()");
        pipelineSource.Should().Contain("operation.PublishedAt > global::System.DateTime.UtcNow");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.NotInFutureAttribute()");
        pipelineSource.Should().Contain("operation.ScheduledAt < global::System.DateTimeOffset.UtcNow");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.NotInPastAttribute()");
        pipelineSource.Should().Contain("operation.ItemIds is not null");
        pipelineSource.Should().Contain("operation.ItemIds.Length == 0");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.NotEmptyListAttribute(\"Add at least one item.\")");
        pipelineSource.Should().Contain("operation.ScoreIds is not null");
        pipelineSource.Should().Contain("operation.ScoreIds.Length > 2");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.MaxItemsListAttribute(2, \"Too many items.\")");
        pipelineSource.Should().Contain("operation.MinimumAge is not null");
        pipelineSource.Should().Contain("operation.MinimumAge.Value < 5");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.GreaterThanOrEqualToAttribute(5, \"Minimum five.\")");
        pipelineSource.Should().Contain("operation.MaximumAge is not null");
        pipelineSource.Should().Contain("operation.MaximumAge.Value > 10");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.LessThanOrEqualToAttribute(10)");
        pipelineSource.Should().Contain("operation.MaximumValue is not null && operation.MinimumValue is not null");
        pipelineSource.Should().Contain("operation.MaximumValue.Value < operation.MinimumValue.Value");
        pipelineSource.Should().Contain("new global::Blueprint.Validation.MustBeGreaterThanOrEqualAttribute(\"MinimumValue\")");
        pipelineSource.Should().Contain("_va_Password.IsValid(operation.Password) == false");
        pipelineSource.Should().Contain("_va_ResourceKey.IsValid(operation.ResourceKey) == false");
        pipelineSource.Should().Contain("_va_Postcode.IsValid(operation.Postcode) == false");

        // Verify static readonly fields are emitted for cached validation attributes
        pipelineSource.Should().Contain("private static readonly Blueprint.Validation.PasswordAttribute _va_Password");
        pipelineSource.Should().Contain("private static readonly Blueprint.Validation.ResourceKeyAttribute _va_ResourceKey");
        pipelineSource.Should().Contain("private static readonly Blueprint.Validation.UkPostcodeAttribute _va_Postcode");
        pipelineSource.IndexOf("operation.AcceptedTerms == false", StringComparison.Ordinal)
            .Should()
            .BeLessThan(pipelineSource.IndexOf("var handlerResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_handler_with_additional_blueprint_validation_attributes_generates_correctly()
    {
        var source = """
                     using System;
                     using Blueprint;
                     using Blueprint.Model;
                     using Blueprint.Validation;
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

                         [MustBeTrue("Accept the terms.")]
                         public bool AcceptedTerms { get; set; }

                         [InFuture("Future only.")]
                         public DateTime? StartsAt { get; set; }

                         [InPast]
                         public DateTimeOffset? CompletedAt { get; set; }

                         [NotInFuture]
                         public DateTime PublishedAt { get; set; }

                         [NotInPast]
                         public DateTimeOffset ScheduledAt { get; set; }

                         [NotEmptyList("Add at least one item.")]
                         public int[]? ItemIds { get; set; }

                         [MaxItemsList(2, "Too many items.")]
                         public int[]? ScoreIds { get; set; }

                         [GreaterThanOrEqualTo(5, "Minimum five.")]
                         public decimal? MinimumAge { get; set; }

                         [LessThanOrEqualTo(10)]
                         public float? MaximumAge { get; set; }

                         public int? MinimumValue { get; set; }

                         [MustBeGreaterThanOrEqual(nameof(MinimumValue))]
                         public int? MaximumValue { get; set; }

                         [Password("Weak password.")]
                         public string? Password { get; set; }

                         [ResourceKey("Bad resource key.")]
                         public string? ResourceKey { get; set; }

                         [UkPostcode("Bad postcode.")]
                         public string? Postcode { get; set; }

                         public static Task<string> HandleAsync(CreateUserCommand command)
                         {
                             return Task.FromResult("ok");
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
