using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Validation;

/// <summary>
/// Pins <c>RequiredItemsIf</c>/<c>RequiredItemsIfNot</c> rejecting collections whose elements are all
/// <see langword="null"/> or whitespace-only, in both the runtime attribute (<see cref="RequiredItemsIfAttribute.IsValid"/>)
/// and the generated fast path (<see cref="Blueprint.SourceGenerator.Modules.ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateItemsRule"/>).
/// Reproduces Signature365 legacy Blueprint commits <c>0df16f1168</c> and <c>b83ddba5d6</c>: a collection of
/// whitespace-only strings must fail the requirement, not just an empty or null collection.
/// </summary>
public class RequiredItemsIfBlankElementTests
{
    // --- Runtime attribute path (RequiredItemsIfAttribute.IsValid via reflection) ---

    [Fact]
    public void RequiredItemsIf_when_all_elements_blank_then_invalid()
    {
        var model = new BlankElementModel { RestrictFormats = true, Formats = ["  ", "\t", string.Empty] };
        var context = new ValidationContext(model) { MemberName = nameof(BlankElementModel.Formats) };
        var attr = new RequiredItemsIfAttribute(nameof(BlankElementModel.RestrictFormats), true);

        var result = attr.GetValidationResult(model.Formats, context);

        result.Should().NotBe(ValidationResult.Success);
    }

    [Fact]
    public void RequiredItemsIf_when_any_element_null_or_blank_then_invalid()
    {
        var model = new BlankElementModel { RestrictFormats = true, Formats = ["pdf", null!, " "] };
        var context = new ValidationContext(model) { MemberName = nameof(BlankElementModel.Formats) };
        var attr = new RequiredItemsIfAttribute(nameof(BlankElementModel.RestrictFormats), true);

        var result = attr.GetValidationResult(model.Formats, context);

        result.Should().NotBe(ValidationResult.Success);
    }

    [Fact]
    public void RequiredItemsIf_when_all_elements_non_blank_then_valid()
    {
        var model = new BlankElementModel { RestrictFormats = true, Formats = ["pdf", "csv"] };
        var context = new ValidationContext(model) { MemberName = nameof(BlankElementModel.Formats) };
        var attr = new RequiredItemsIfAttribute(nameof(BlankElementModel.RestrictFormats), true);

        var result = attr.GetValidationResult(model.Formats, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void RequiredItemsIfNot_when_all_elements_blank_then_invalid()
    {
        var model = new BlankElementNotModel { Mode = "Some", Selected = ["   "] };
        var context = new ValidationContext(model) { MemberName = nameof(BlankElementNotModel.Selected) };
        var attr = new RequiredItemsIfNotAttribute(nameof(BlankElementNotModel.Mode), "All");

        var result = attr.GetValidationResult(model.Selected, context);

        result.Should().NotBe(ValidationResult.Success);
    }

    private sealed class BlankElementModel
    {
        public bool RestrictFormats { get; set; }

        [RequiredItemsIf(nameof(RestrictFormats), true)]
        public List<string>? Formats { get; set; }
    }

    private sealed class BlankElementNotModel
    {
        public string? Mode { get; set; }

        [RequiredItemsIfNot(nameof(Mode), "All")]
        public List<string>? Selected { get; set; }
    }

    // --- Generated fast-path (ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateItemsRule) ---

    [Fact]
    public async Task When_required_items_if_generated_pipeline_evaluates_blank_only_collection_then_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfBlankElementValidationOperation));
        var context = CreateContext(app, RequiredItemsIfBlankElementValidationOperation.Configure());
        var operation = (RequiredItemsIfBlankElementValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Roles = ["  ", "\t"];

        RequiredItemsIfBlankElementValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredItemsIfBlankElementValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredItemsIfBlankElementValidationOperation.Roles));
    }

    [Fact]
    public async Task When_required_items_if_generated_pipeline_evaluates_populated_collection_then_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfBlankElementValidationOperation));
        var context = CreateContext(app, RequiredItemsIfBlankElementValidationOperation.Configure());
        var operation = (RequiredItemsIfBlankElementValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Roles = ["admin"];

        RequiredItemsIfBlankElementValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredItemsIfBlankElementValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_required_items_if_generated_pipeline_evaluates_value_type_collection_then_runs_handler()
    {
        // Value-type elements (int) exercise the generator's Cast<object> lowering, which must box rather than
        // assume a reference-type element. The type argument is deliberately unannotated: an explicit nullable
        // annotation in generated code would need a surrounding #nullable context that pipeline files do not declare.
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfIntValidationOperation));
        var context = CreateContext(app, RequiredItemsIfIntValidationOperation.Configure());
        var operation = (RequiredItemsIfIntValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Counts = [1, 2, 3];

        RequiredItemsIfIntValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredItemsIfIntValidationOperation.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(NullServiceProvider.Instance, app, descriptor, CancellationToken.None);
    }
}

public partial class RequiredItemsIfBlankElementValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public bool Enabled { get; set; }

    [RequiredItemsIf(nameof(Enabled), true)]
    public List<string>? Roles { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredItemsIfBlankElementValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Roles is null ? string.Empty : string.Join(",", operation.Roles));
    }
}

public partial class RequiredItemsIfIntValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public bool Enabled { get; set; }

    [RequiredItemsIf(nameof(Enabled), true)]
    public List<int>? Counts { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredItemsIfIntValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(string.Empty);
    }
}
