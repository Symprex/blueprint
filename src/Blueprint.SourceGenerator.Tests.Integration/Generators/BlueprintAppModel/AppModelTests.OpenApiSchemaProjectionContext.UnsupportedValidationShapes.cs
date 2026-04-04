using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiSchemaProjectionContextUnsupportedValidationShapesTests
{
    [Fact]
    public void When_serializer_backed_schema_uses_compare_attribute_then_openapi_schema_projection_fails_fast()
    {
        var app = new UnsupportedValidationBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var act = () => OpenApiSchemaProjectionContext.Create(documentContext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*body type 'Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.UnsupportedValidationSchema'*property 'Password'*CompareAttribute*");
    }

    [Fact]
    public void When_serializer_backed_schema_uses_greater_than_property_attribute_then_openapi_schema_projection_fails_fast()
    {
        var app = new UnsupportedGreaterThanPropertyBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var act = () => OpenApiSchemaProjectionContext.Create(documentContext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*body type 'Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.UnsupportedGreaterThanPropertySchema'*property 'Maximum'*GreaterThanPropertyAttribute*");
    }

    private sealed class UnsupportedValidationBlueprintApp : BlueprintApp
    {
        public UnsupportedValidationBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(UnsupportedValidationOperationShell), "UnsupportedValidation", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(UnsupportedValidationSchema).FullName!]));

            this.AppOwnedSerializerContext = new UnsupportedValidationJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(UnsupportedValidationJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(UnsupportedValidationSchema)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(UnsupportedValidationSchema).FullName!];

            this.Register(descriptor);
        }
    }

    private sealed class UnsupportedGreaterThanPropertyBlueprintApp : BlueprintApp
    {
        public UnsupportedGreaterThanPropertyBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(UnsupportedGreaterThanPropertyOperationShell), "UnsupportedGreaterThanProperty", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(UnsupportedGreaterThanPropertySchema).FullName!]));

            this.AppOwnedSerializerContext = new UnsupportedGreaterThanPropertyJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(UnsupportedGreaterThanPropertyJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(UnsupportedGreaterThanPropertySchema)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(UnsupportedGreaterThanPropertySchema).FullName!];

            this.Register(descriptor);
        }
    }

    private sealed class UnsupportedValidationOperationShell;

    private sealed class UnsupportedGreaterThanPropertyOperationShell;
}

public sealed class UnsupportedValidationSchema
{
    public string? Confirmation { get; init; }

    [Compare(nameof(Confirmation))]
    public string? Password { get; init; }
}

[JsonSerializable(typeof(UnsupportedValidationSchema))]
public partial class UnsupportedValidationJsonContext : JsonSerializerContext;

public sealed class UnsupportedGreaterThanPropertySchema
{
    public int Minimum { get; init; }

    [GreaterThanProperty(nameof(Minimum))]
    public int Maximum { get; init; }
}

[JsonSerializable(typeof(UnsupportedGreaterThanPropertySchema))]
public partial class UnsupportedGreaterThanPropertyJsonContext : JsonSerializerContext;
