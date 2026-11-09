using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Resolves the HTTP methods an operation is exposed with: the one rule <see cref="OperationHttpMethodMetadataModule"/>
/// publishes on its descriptor, which <c>BlueprintHttpClient&lt;TApp&gt;</c> sends the first of, and which a generated typed
/// client sends as a constant, so the two always send the same method.
/// </summary>
/// <remarks>
/// App-level diagnostics reach it through <see cref="HttpHostExposureHelper.ResolveHttpMethods"/>.
/// </remarks>
internal static class OperationHttpMethodResolver
{
    private const string HttpMethodAttributeMetadataName = "Blueprint.Model.Attributes.HttpMethodAttribute";
    private const string HttpGetAttributeMetadataName = "Blueprint.Model.Attributes.HttpGetAttribute";
    private const string HttpPostAttributeMetadataName = "Blueprint.Model.Attributes.HttpPostAttribute";
    private const string HttpPutAttributeMetadataName = "Blueprint.Model.Attributes.HttpPutAttribute";
    private const string HttpPatchAttributeMetadataName = "Blueprint.Model.Attributes.HttpPatchAttribute";
    private const string HttpDeleteAttributeMetadataName = "Blueprint.Model.Attributes.HttpDeleteAttribute";

    /// <summary>
    /// Resolves the operation's HTTP methods: the distinct, upper-cased methods its HTTP method attributes declare, in
    /// ordinal order, or POST for a command and GET for anything else when it declares none.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    /// <returns>The methods, or why there are none.</returns>
    internal static OperationHttpMethods Resolve(INamedTypeSymbol operationType)
    {
        var explicitMethods = operationType.GetAttributes()
            .Where(static attribute => attribute.AttributeClass is { } attributeClass && IsOrInheritsFromHttpMethodAttribute(attributeClass))
            .Select(static attribute => (Attribute: attribute, HttpMethod: GetExplicitHttpMethodValue(attribute)))
            .ToArray();

        if (explicitMethods.Length == 0)
        {
            return new OperationHttpMethods(IsCommand(operationType) ? ["POST"] : ["GET"], null, null);
        }

        foreach (var explicitMethod in explicitMethods)
        {
            if (Normalize(explicitMethod.HttpMethod) is null)
            {
                return new OperationHttpMethods([], explicitMethod.Attribute, explicitMethods[0].Attribute);
            }
        }

        var methods = explicitMethods
            .Select(static explicitMethod => Normalize(explicitMethod.HttpMethod)!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
            .ToArray();

        return new OperationHttpMethods(methods, null, explicitMethods[0].Attribute);
    }

    private static string? Normalize(string? httpMethod)
    {
        return httpMethod is null || string.IsNullOrWhiteSpace(httpMethod)
            ? null
            : httpMethod.Trim().ToUpperInvariant();
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

    /// <summary>
    /// The expression naming the shared <see cref="System.Net.Http.HttpMethod"/> instance of a standard method, so generated
    /// code that sends an operation references it rather than making or parsing one per call.
    /// </summary>
    /// <param name="httpMethod">A method <see cref="Resolve"/> returned, upper-cased.</param>
    /// <returns>The expression, or <see langword="null"/> for a method <see cref="System.Net.Http.HttpMethod"/> has no instance of.</returns>
    internal static string? StandardHttpMethodExpression(string httpMethod)
    {
        return httpMethod switch
        {
            "GET" => "global::System.Net.Http.HttpMethod.Get",
            "POST" => "global::System.Net.Http.HttpMethod.Post",
            "PUT" => "global::System.Net.Http.HttpMethod.Put",
            "PATCH" => "global::System.Net.Http.HttpMethod.Patch",
            "DELETE" => "global::System.Net.Http.HttpMethod.Delete",
            "HEAD" => "global::System.Net.Http.HttpMethod.Head",
            "OPTIONS" => "global::System.Net.Http.HttpMethod.Options",
            "TRACE" => "global::System.Net.Http.HttpMethod.Trace",
            _ => null,
        };
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.IsInNamespace("Blueprint", "Model")
            && candidate.Name == "ICommand");
    }
}

/// <summary>
/// The HTTP methods <see cref="OperationHttpMethodResolver"/> resolved for an operation.
/// </summary>
/// <param name="Methods">
/// The methods, distinct and in ordinal order, the first of which a client sends; empty when <paramref name="InvalidAttribute"/>
/// declares an empty one.
/// </param>
/// <param name="InvalidAttribute">The attribute declaring a null, empty or whitespace method, or <see langword="null"/> for none.</param>
/// <param name="FirstExplicitAttribute">The first HTTP method attribute declared, or <see langword="null"/> when none is.</param>
internal readonly record struct OperationHttpMethods(string[] Methods, AttributeData? InvalidAttribute, AttributeData? FirstExplicitAttribute);
