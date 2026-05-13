using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.OpenApi;

/// <summary>
/// Contributes <c>httpRouting.methods</c> and <c>httpRouting.explicit</c> to the per-operation debug manifest,
/// reflecting the HTTP method(s) the operation declares (via explicit attributes) or inherits (by convention).
/// </summary>
/// <remarks>
/// This module independently reads <c>[HttpGet]</c>, <c>[HttpPost]</c>, and other HTTP method attributes
/// from the operation type symbol rather than depending on <see cref="OperationHttpMethodMetadataModule"/>
/// metadata, which gates on HTTP host exposure. The manifest should surface routing intent regardless
/// of whether the full host chain is active.
/// </remarks>
internal sealed class HttpMethodManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    private const string HttpMethodAttributeNamespace = "Blueprint.Model.Attributes";

    /// <inheritdoc />
    public string ModuleId => nameof(HttpMethodManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HttpMethodManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var explicitMethods = GetExplicitHttpMethods(operationType);

        if (explicitMethods.Length > 0)
        {
            var methods = explicitMethods
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static m => m, StringComparer.Ordinal)
                .ToArray();

            context.AddEntry("httpRouting.methods", new JArray(methods.Select(static m => new JValue(m))));
            context.AddEntry("httpRouting.explicit", new JValue(true));
        }
        else
        {
            var conventionalMethod = IsCommand(operationType) ? "POST" : "GET";
            context.AddEntry("httpRouting.methods", new JArray(new JValue(conventionalMethod)));
            context.AddEntry("httpRouting.explicit", new JValue(false));
        }
    }

    private static string[] GetExplicitHttpMethods(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Where(static attribute => IsHttpMethodAttribute(attribute))
            .Select(static attribute => ResolveHttpMethodValue(attribute))
            .Where(static method => method is not null)
            .Select(static method => method!)
            .ToArray();
    }

    private static bool IsHttpMethodAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { } attributeClass)
        {
            return false;
        }

        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (current.Name == "HttpMethodAttribute"
                && current.ContainingNamespace.ToDisplayString() == HttpMethodAttributeNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveHttpMethodValue(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { } attrClass
            || attrClass.ContainingNamespace.ToDisplayString() != HttpMethodAttributeNamespace)
        {
            return attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null;
        }

        return attrClass.Name switch
        {
            "HttpGetAttribute" => "GET",
            "HttpPostAttribute" => "POST",
            "HttpPutAttribute" => "PUT",
            "HttpPatchAttribute" => "PATCH",
            "HttpDeleteAttribute" => "DELETE",
            _ => attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null,
        };
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == "ICommand"
            && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model");
    }
}
