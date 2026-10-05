using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Pins <see cref="Blueprint.Model.Expandable{T}"/>'s <c>value</c> property against legacy Blueprint's rendering of
/// the same shape (Signature365 8b40ecc961 committed Portal document, <c>ExpandableOfAzureAdGroup.value</c>): a
/// <c>oneOf</c> reference carrying a description, without <c>"nullable": true</c>, even though <c>T?</c> is nullable.
/// </summary>
/// <remarks>
/// NJsonSchema described legacy's generic type parameter <c>T?</c> as non-nullable on the wire (the closed generic's
/// reflected nullability metadata for an unconstrained type parameter did not carry through), even though every other
/// nullable object reference legacy rendered (e.g. Portal's <c>TemplateFunctionSymbol.example</c>) does carry
/// <c>"nullable": true</c> beside its <c>oneOf</c>. Reproduced only for consumers on <see cref="OpenApiSchemaNaming.Legacy"/>;
/// a <see cref="OpenApiSchemaNaming.Default"/> consumer reasonably wants the correct nullability.
/// </remarks>
public class OpenApiExpandableNullabilityTests
{
    [Fact]
    public void Legacy_naming_renders_Expandable_value_as_oneOf_without_nullable()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        var value = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ExpandableOfDepartmentResource").GetProperty("properties").GetProperty("Value");

        value.TryGetProperty("nullable", out _).Should().BeFalse("legacy never rendered \"nullable\" for Expandable<T>.Value");
        value.GetProperty("oneOf")[0].GetProperty("$ref").GetString().Should().Be("#/components/schemas/DepartmentResource");
    }

    [Fact]
    public void Default_naming_still_renders_Expandable_value_as_nullable()
    {
        using var document = Render(static _ => { });

        var value = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ExpandableOfDepartment").GetProperty("properties").GetProperty("Value");

        value.GetProperty("nullable").GetBoolean().Should().BeTrue("a Default consumer wants the correct nullability, not legacy's artefact");
    }

    [Fact]
    public void Legacy_naming_renders_Expandable_value_as_non_nullable_when_the_app_serialises_camelCase()
    {
        // Signature365 serialises camelCase, so the serialised name (value) is not the CLR name (Value) the open generic
        // definition is looked up by; the integration app above serialises PascalCase, where the two coincide.
        var documentContext = OpenApiDocumentBuilderContext.Create(new CamelCaseExpandableBlueprintApp());

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext, OpenApiSchemaNaming.Legacy);
        var expandable = schemaContext.Schemas.Single(static schema => schema.BodyTypeName.StartsWith("ExpandableOf", StringComparison.Ordinal));

        var value = expandable.Properties.Single(static property => property.Name == "value");
        value.IsNullable.Should().BeFalse("legacy documented Expandable<T>.Value as non-nullable whatever the serialised casing");
    }

    private static JsonDocument Render(Action<OpenApiInfoOptions> configure)
    {
        var options = new OpenApiInfoOptions();
        configure(options);

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }

    private sealed class CamelCaseExpandableBlueprintApp : BlueprintApp
    {
        public CamelCaseExpandableBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(CamelCaseExpandableOperationShell), "CamelCaseExpandable", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(CamelCaseExpandableEmployee).FullName!]));

            this.AppOwnedSerializerContext = CamelCaseExpandableJsonContext.Default;
            this.AppOwnedSerializerContextType = typeof(CamelCaseExpandableJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(CamelCaseExpandableEmployee)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(CamelCaseExpandableEmployee).FullName!];

            this.Register(descriptor);
        }
    }

    private sealed class CamelCaseExpandableOperationShell;
}

public sealed class CamelCaseExpandableDepartment : ApiResource
{
    public override string Object => "CamelCaseExpandableDepartment";

    public string Name { get; set; } = string.Empty;
}

public sealed class CamelCaseExpandableEmployee : ApiResource
{
    public override string Object => "CamelCaseExpandableEmployee";

    public Expandable<CamelCaseExpandableDepartment> Department { get; set; } = new("dept-1");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CamelCaseExpandableEmployee))]
[JsonSerializable(typeof(Expandable<CamelCaseExpandableDepartment>))]
[JsonSerializable(typeof(CamelCaseExpandableDepartment))]
public partial class CamelCaseExpandableJsonContext : JsonSerializerContext;
