using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Helper class for analyzing handler method parameters and determining how they should be resolved.
/// </summary>
internal static class ParameterAnalyzer
{
    /// <summary>
    /// Well-known handler method names.
    /// </summary>
    public static readonly string[] HandlerMethodNames = { "Invoke", "InvokeAsync", "Handle", "HandleAsync" };

    /// <summary>
    /// Analyzes the parameters of a handler method and creates HandlerParameter instances.
    /// </summary>
    /// <param name="handlerMethod">The handler method to analyze.</param>
    /// <param name="operationType">The operation type symbol.</param>
    /// <param name="isExternalHandler">Whether this is an external handler (first param is operation).</param>
    /// <returns>List of analyzed parameters.</returns>
    public static EquatableList<HandlerParameter> AnalyzeParameters(
        IMethodSymbol handlerMethod,
        INamedTypeSymbol operationType,
        bool isExternalHandler)
    {
        var parameters = new List<HandlerParameter>();
        var methodParams = handlerMethod.Parameters;

        // For external handlers, skip the first parameter (it's the operation itself)
        var startIndex = isExternalHandler ? 1 : 0;

        for (var i = startIndex; i < methodParams.Length; i++)
        {
            var param = methodParams[i];
            var source = DetermineParameterSource(param, operationType);
            var contextPropertyName = source == ParameterSource.FromContextProperty
                ? GetContextPropertyName(param)
                : null;

            parameters.Add(new HandlerParameter(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                source,
                contextPropertyName));
        }

        return new EquatableList<HandlerParameter>(parameters.ToArray());
    }

    /// <summary>
    /// Determines how a parameter should be resolved.
    /// </summary>
    private static ParameterSource DetermineParameterSource(IParameterSymbol param, INamedTypeSymbol operationType)
    {
        var paramType = param.Type;
        var paramTypeName = paramType.ToDisplayString();

        // Check if it's the operation itself
        if (SymbolEqualityComparer.Default.Equals(paramType, operationType))
        {
            return ParameterSource.Operation;
        }

        // Check for ApiOperationContext
        if (paramTypeName == "Blueprint.Runtime.Exec.ApiOperationContext")
        {
            return ParameterSource.FromContext;
        }

        // Check for known context properties
        if (IsContextProperty(paramTypeName, param.Name))
        {
            return ParameterSource.FromContextProperty;
        }

        // Everything else is resolved from DI
        return ParameterSource.FromDependencyInjection;
    }

    /// <summary>
    /// Checks if a parameter type/name combination represents a context property.
    /// </summary>
    private static bool IsContextProperty(string typeName, string paramName)
    {
        // Match by type
        switch (typeName)
        {
            case "Blueprint.Model.BlueprintApp":
            case "System.Threading.CancellationToken":
                return true;
        }

        // Match by parameter name convention (case-insensitive)
        var lowerName = paramName.ToLowerInvariant();
        return lowerName switch
        {
            "datamodel" or "app" or "cancellationtoken" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Gets the property name to access from ApiOperationContext for a given parameter.
    /// </summary>
    private static string GetContextPropertyName(IParameterSymbol param)
    {
        var typeName = param.Type.ToDisplayString();

        return typeName switch
        {
            "Blueprint.Model.BlueprintApp" => "App",
            "System.Threading.CancellationToken" => "OperationCancelled",
            _ => param.Name.ToLowerInvariant() switch
            {
                "datamodel" or "app" => "App",
                "cancellationtoken" => "OperationCancelled",
                _ => "App", // Default fallback
            },
        };
    }

    /// <summary>
    /// Checks if a method is a valid handler method by name.
    /// </summary>
    public static bool IsHandlerMethodName(string methodName)
    {
        return HandlerMethodNames.Contains(methodName);
    }
}
