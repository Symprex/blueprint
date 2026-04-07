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
    private const string HttpMethodAttributeMetadataName = "Blueprint.Model.Attributes.HttpMethodAttribute";
    private const string HttpGetAttributeMetadataName = "Blueprint.Model.Attributes.HttpGetAttribute";
    private const string HttpPostAttributeMetadataName = "Blueprint.Model.Attributes.HttpPostAttribute";
    private const string HttpPutAttributeMetadataName = "Blueprint.Model.Attributes.HttpPutAttribute";
    private const string HttpPatchAttributeMetadataName = "Blueprint.Model.Attributes.HttpPatchAttribute";
    private const string HttpDeleteAttributeMetadataName = "Blueprint.Model.Attributes.HttpDeleteAttribute";

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
        if (HasHttpHostExposure(context) == false)
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

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static bool TryResolveHttpMethods(DescriptorContributorContext context, out string[] httpMethods)
    {
        var operationType = context.GetOperationTypeSymbol();
        var explicitMethods = GetExplicitHttpMethods(operationType);

        if (explicitMethods.Length > 0)
        {
            foreach (var explicitMethod in explicitMethods)
            {
                if (NormalizeHttpMethod(explicitMethod.HttpMethod) is not null)
                {
                    continue;
                }

                context.AddDiagnostic(
                    new DiagnosticInfo(
                        OperationHttpMethodMetadataDiagnosticDescriptors.InvalidExplicitHttpMethod,
                        GetExplicitHttpMethodLocation(explicitMethod.Attribute, operationType),
                        [operationType.Name]));
                httpMethods = [];
                return false;
            }

            httpMethods = explicitMethods
                .Select(static explicitMethod => NormalizeHttpMethod(explicitMethod.HttpMethod)!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        httpMethods = IsCommand(operationType)
            ? ["POST"]
            : ["GET"];
        return true;
    }

    private static (AttributeData Attribute, string? HttpMethod)[] GetExplicitHttpMethods(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Where(static attribute => IsExplicitHttpMethodAttribute(attribute))
            .Select(static attribute => (Attribute: attribute, HttpMethod: GetExplicitHttpMethodValue(attribute)))
            .ToArray();
    }

    private static bool IsExplicitHttpMethodAttribute(AttributeData attribute)
    {
        return attribute.AttributeClass is { } attributeClass
            && IsOrInheritsFromHttpMethodAttribute(attributeClass);
    }

    private static string? NormalizeHttpMethod(string? httpMethod)
    {
        if (httpMethod is null || string.IsNullOrWhiteSpace(httpMethod))
        {
            return null;
        }

        return httpMethod.Trim().ToUpperInvariant();
    }

    private static string? GetExplicitHttpMethodValue(AttributeData attribute)
    {
        return attribute.AttributeClass?.ToFullMetadataName() switch
        {
            HttpGetAttributeMetadataName => "GET",
            HttpPostAttributeMetadataName => "POST",
            HttpPutAttributeMetadataName => "PUT",
            HttpPatchAttributeMetadataName => "PATCH",
            HttpDeleteAttributeMetadataName => "DELETE",
            _ => attribute.ConstructorArguments.FirstOrDefault().Value as string,
        };
    }

    private static bool IsOrInheritsFromHttpMethodAttribute(INamedTypeSymbol attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToFullMetadataName(), HttpMethodAttributeMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
            && candidate.Name == "ICommand");
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
