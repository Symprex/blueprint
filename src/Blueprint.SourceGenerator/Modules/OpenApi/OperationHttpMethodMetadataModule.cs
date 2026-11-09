using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects descriptor-published HTTP method metadata for HTTP-exposed operations so later OpenAPI slices can reason from
/// stable descriptor facts instead of rediscovering explicit attributes or host conventions at runtime.
/// </summary>
public sealed class OperationHttpMethodMetadataModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(OperationHttpMethodMetadataModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(OperationHttpMethodMetadataModule);

    /// <inheritdoc />
    public int Order => 65;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (HttpHostExposureHelper.HasHttpHostExposure(operationType) == false)
        {
            return;
        }

        if (TryResolveHttpMethods(context, out var httpMethods) == false || httpMethods.Length == 0)
        {
            return;
        }

        foreach (var httpMethod in httpMethods)
        {
            context.AddMetadata(new OperationHttpMethodMetadataRegistration(httpMethod));
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<OperationHttpMethodMetadataRegistration>()));
    }

    private static bool TryResolveHttpMethods(DescriptorContributorContext context, out string[] httpMethods)
    {
        var operationType = context.GetOperationTypeSymbol();
        var resolved = OperationHttpMethodResolver.Resolve(operationType);

        if (resolved.InvalidAttribute is { } invalidAttribute)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    OperationHttpMethodMetadataDiagnosticDescriptors.InvalidExplicitHttpMethod,
                    GetExplicitHttpMethodLocation(invalidAttribute, operationType),
                    [operationType.Name]));
            httpMethods = [];
            return false;
        }

        httpMethods = resolved.Methods;

        if (httpMethods.Length > 1 && resolved.FirstExplicitAttribute is { } firstExplicitAttribute)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    OperationHttpMethodMetadataDiagnosticDescriptors.ConflictingHttpMethods,
                    GetExplicitHttpMethodLocation(firstExplicitAttribute, operationType),
                    [operationType.Name, string.Join(", ", httpMethods)]));
        }

        return true;
    }

    private static LocationInfo? GetExplicitHttpMethodLocation(AttributeData attribute, INamedTypeSymbol operationType)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? operationType.Locations.FirstOrDefault(static candidate => candidate.IsInSource);

        return location is null ? null : LocationInfo.CreateFrom(location);
    }

    private static readonly Dictionary<string, string> WellKnownSingleMethodReferences = new(StringComparer.Ordinal)
    {
        ["DELETE"] = "global::Blueprint.Model.WellKnownFeatureData.HttpDelete",
        ["GET"] = "global::Blueprint.Model.WellKnownFeatureData.HttpGet",
        ["PATCH"] = "global::Blueprint.Model.WellKnownFeatureData.HttpPatch",
        ["POST"] = "global::Blueprint.Model.WellKnownFeatureData.HttpPost",
        ["PUT"] = "global::Blueprint.Model.WellKnownFeatureData.HttpPut",
    };

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<OperationHttpMethodMetadataRegistration> registrations)
    {
        var distinctMethods = registrations
            .Select(static registration => registration.HttpMethod)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
            .ToArray();

        if (distinctMethods.Length == 0)
        {
            return;
        }

        if (distinctMethods.Length == 1 && WellKnownSingleMethodReferences.TryGetValue(distinctMethods[0], out var wellKnownRef))
        {
            codeWriter.AppendLine($"this.SetFeatureData({wellKnownRef});");
            return;
        }

        var renderedMethods = distinctMethods
            .Select(static httpMethod => CSharpSymbolDisplay.FormatLiteral(httpMethod, quote: true))
            .ToArray();

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHttpMethodMetadataFeatureData("
            + $"global::System.Collections.Immutable.ImmutableArray.Create({string.Join(", ", renderedMethods)})));");
    }
}
