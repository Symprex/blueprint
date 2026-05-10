using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

internal sealed class ValidationConstraintBlueprintApp : BlueprintApp
{
    public ValidationConstraintBlueprintApp()
    {
        var descriptor = new ApiOperationDescriptor(typeof(ValidationConstraintOperationShell), "SchemaValidationConstraints", "test-fixture");
        descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
        descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(SchemaValidationConstraintsResponse).FullName!]));

        this.AppOwnedSerializerContext = new SchemaValidationConstraintsJsonContext(new JsonSerializerOptions());
        this.AppOwnedSerializerContextType = typeof(SchemaValidationConstraintsJsonContext);
        this.AppOwnedSerializerContextBodyTypes = [typeof(SchemaValidationConstraintsResponse)];
        this.AppOwnedSerializerContextBodyTypeNames = [typeof(SchemaValidationConstraintsResponse).FullName!];

        this.Register(descriptor);
    }
}

internal sealed class ValidationConstraintOperationShell;

public sealed class SchemaValidationConstraintsResponse
{
    [StringLength(12, MinimumLength = 4)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Code { get; init; }

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; init; }

    [GreaterThan(0)]
    public int PositiveCount { get; init; }

    [Range(1, 10)]
    public int Quantity { get; init; }

    [MinLength(1)]
    [MaxLength(20)]
    public List<string> Tags { get; init; } = [];
}

[JsonSerializable(typeof(SchemaValidationConstraintsResponse))]
public partial class SchemaValidationConstraintsJsonContext : JsonSerializerContext;
