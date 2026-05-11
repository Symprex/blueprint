using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Routes handler invocation through the shared pipeline contributor seam.
/// </summary>
public sealed class HandlerExecutionModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <summary>
    /// The shared variable purpose used when downstream contributors need the handler result.
    /// </summary>
    public const string HandlerResultPurpose = "handler-result";

    /// <summary>
    /// The normalized type name that all handler results are assigned as after the execution stage,
    /// allowing PostExecution contributors to consume the result without knowing the specific result subtype.
    /// </summary>
    public const string HandlerResultTypeName = "Blueprint.OperationResult";

    /// <inheritdoc />
    public string ModuleId => nameof(HandlerExecutionModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HandlerExecutionModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var handler = context.Handler ?? throw new InvalidOperationException("The pipeline contributor requires handler metadata, but no handler was resolved for the current operation.");
        var normalization = CreateReturnNormalization(context, handler);

        if (normalization is null)
        {
            return;
        }

        CacheKnownSingletonServices(context, handler);

        context.AddRenderStep(PipelineStage.Execution, renderContext => RenderHandlerInvocation(renderContext, normalization));
        context.RegisterVariable(
            new VariableKey(HandlerResultTypeName, HandlerResultPurpose),
            availableFrom: PipelineStage.Execution,
            sourceKind: VariableSourceKind.ProducedValue,
            lifetime: VariableLifetime.Local);
    }

    private static void RenderHandlerInvocation(PipelineRenderContext context, HandlerReturnNormalization normalization)
    {
        foreach (var param in context.GetHandlerParameters())
        {
            switch (param.Source)
            {
                case ParameterSource.FromDependencyInjection:
                    // Use constructor-cached field for known singletons (e.g. ILogger<T>)
                    var serviceExpr = context.TryGetCachedServiceFieldExpression(param.ParameterTypeName, out var cachedExpr)
                        ? cachedExpr
                        : context.GetRequiredServiceExpression(param.ParameterTypeName);

                    context.DeclareVariable(param.VariableName, serviceExpr);
                    break;
            }
        }

        if (context.GetHandlerParameters().Any(static p => p.Source == ParameterSource.FromDependencyInjection))
        {
            context.WriteBlankLine();
        }

        var parameterList = new List<string>();
        foreach (var param in context.GetHandlerParameters())
        {
            switch (param.Source)
            {
                case ParameterSource.Operation:
                    parameterList.Add(context.GetOperationExpression());
                    break;
                case ParameterSource.FromContext:
                    parameterList.Add(context.GetContextExpression());
                    break;
                case ParameterSource.FromContextProperty:
                    parameterList.Add(context.GetContextPropertyExpression(param.ContextPropertyName!));
                    break;
                case ParameterSource.FromDependencyInjection:
                    parameterList.Add(param.VariableName);
                    break;
            }
        }

        string invocationExpression;
        if (context.IsInlineHandler())
        {
            invocationExpression = context.IsStaticHandler()
                ? $"{context.GetOperationTypeFullName()}.{context.GetHandlerMethodName()}({string.Join(", ", parameterList)})"
                : $"{context.GetOperationExpression()}.{context.GetHandlerMethodName()}({string.Join(", ", parameterList)})";
        }
        else
        {
            var handlerClassName = context.GetRequiredHandlerClassName();
            var handlerClassFullName = context.GetRequiredHandlerClassFullName();
            var handlerVarName = char.ToLowerInvariant(handlerClassName[0]) + handlerClassName.Substring(1);

            context.DeclareVariable(
                handlerVarName,
                context.GetOptionalServiceExpression(handlerClassFullName));
            context.If(
                $"{handlerVarName} == null",
                branch =>
                {
                    branch.Assign(
                        handlerVarName,
                        branch.GetCompiledHandlerConstructorExpression(handlerClassFullName));
                });

            context.WriteBlankLine();

            var allParams = new List<string> { context.GetOperationExpression() };
            allParams.AddRange(parameterList);
            invocationExpression = $"{handlerVarName}.{context.GetHandlerMethodName()}({string.Join(", ", allParams)})";
        }

        var configuredAwait = context.IsHandlerAsync()
            ? $"await {invocationExpression}.ConfigureAwait(false)"
            : invocationExpression;

        switch (normalization.Kind)
        {
            case HandlerReturnNormalizationKind.NoResult:
                context.WriteStatement($"{configuredAwait};");
                context.DeclareVariable("handlerResult", "(Blueprint.OperationResult)new Blueprint.Runtime.Exec.NoResultOperationResult()");
                break;
            case HandlerReturnNormalizationKind.OperationResult:
                context.DeclareVariable("handlerResult", configuredAwait);
                break;
            case HandlerReturnNormalizationKind.PlainValue:
                context.DeclareVariable("handlerResult", $"(Blueprint.OperationResult)new Blueprint.Runtime.Exec.OkResult<{normalization.PlainValueTypeName}>({configuredAwait})");
                break;
            case HandlerReturnNormalizationKind.ResultsUnion:
                context.DeclareVariable("handlerUnionResult", configuredAwait);
                context.DeclareVariable("handlerResult", "handlerUnionResult.IsT1 ? (Blueprint.OperationResult)handlerUnionResult.AsT1 : (Blueprint.OperationResult)handlerUnionResult.AsT2");
                break;
            default:
                throw new InvalidOperationException($"Unsupported handler normalization kind '{normalization.Kind}'.");
        }
    }

    private static HandlerReturnNormalization? CreateReturnNormalization(PipelineContributorContext context, OperationHandlerModel handler)
    {
        var handlerMethod = ResolveHandlerMethod(context, handler);
        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult")
            ?? throw new InvalidOperationException("The pipeline contributor could not resolve Blueprint.OperationResult from the current compilation.");
        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult")
            ?? throw new InvalidOperationException("The pipeline contributor could not resolve Blueprint.Runtime.Exec.NoResultOperationResult from the current compilation.");
        var okResultGenericType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult`1")
            ?? throw new InvalidOperationException("The pipeline contributor could not resolve Blueprint.Runtime.Exec.OkResult<T> from the current compilation.");
        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(context.Operation.TypeSymbol);
        var effectiveHandlerReturnType = GetEffectiveHandlerReturnType(handlerMethod.ReturnType);

        if (declaredContractType is not null
            && OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var declaredUnionMembers)
            && TryValidateResultsUnionMembers(context, handlerMethod, declaredUnionMembers, operationResultType) == false)
        {
            return null;
        }

        if (effectiveHandlerReturnType is null)
        {
            if (IsCompatibleWithDeclaredContract(handlerMethod, noResultType, declaredContractType, noResultType, operationResultType, context, allowMissingContract: true) == false)
            {
                return null;
            }

            return new HandlerReturnNormalization(
                HandlerReturnNormalizationKind.NoResult,
                noResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                null);
        }

        if (OperationResultContractHelper.TryGetResultsUnionMembers(effectiveHandlerReturnType, out var handlerUnionMembers))
        {
            if (TryValidateResultsUnionMembers(context, handlerMethod, handlerUnionMembers, operationResultType) == false)
            {
                return null;
            }

            if (declaredContractType is null || SymbolEqualityComparer.Default.Equals(declaredContractType, effectiveHandlerReturnType) == false)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        DiagnosticDescriptors.IncompatibleHandlerReturnContract,
                        GetReturnTypeLocation(handlerMethod),
                        [handlerMethod.Name, effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), declaredContractType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "no response"]));
                return null;
            }

            return new HandlerReturnNormalization(
                HandlerReturnNormalizationKind.ResultsUnion,
                effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                null);
        }

        if (OperationResultContractHelper.IsOperationResultType(effectiveHandlerReturnType, operationResultType))
        {
            if (IsCompatibleWithDeclaredContract(handlerMethod, effectiveHandlerReturnType, declaredContractType, noResultType, operationResultType, context, allowMissingContract: true) == false)
            {
                return null;
            }

            return new HandlerReturnNormalization(
                HandlerReturnNormalizationKind.OperationResult,
                effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                null);
        }

        if (IsNullablePlainValue(effectiveHandlerReturnType))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.NullablePlainValueHandlerReturn,
                    GetReturnTypeLocation(handlerMethod),
                    [handlerMethod.Name, effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return null;
        }

        // Background tasks do not return meaningful values to callers; treat a plain value return
        // as NoResult and warn that the value will be discarded rather than failing the pipeline.
        if (IsBackgroundTask(context.Operation.TypeSymbol))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    BackgroundTaskDiagnosticDescriptors.TaskHandlerReturnsDiscardedValue,
                    GetReturnTypeLocation(handlerMethod),
                    [context.Operation.TypeSymbol.Name, effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));

            return new HandlerReturnNormalization(
                HandlerReturnNormalizationKind.NoResult,
                noResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                null);
        }

        var typedOkResult = okResultGenericType.Construct(effectiveHandlerReturnType);
        if (IsCompatibleWithDeclaredContract(handlerMethod, typedOkResult, declaredContractType, noResultType, operationResultType, context, allowMissingContract: false, plainValueType: effectiveHandlerReturnType) == false)
        {
            return null;
        }

        return new HandlerReturnNormalization(
            HandlerReturnNormalizationKind.PlainValue,
            typedOkResult.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            effectiveHandlerReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static IMethodSymbol ResolveHandlerMethod(PipelineContributorContext context, OperationHandlerModel handler)
    {
        if (handler.IsInline)
        {
            return context.Operation.TypeSymbol.GetMembers(handler.HandlerMethodName)
                .OfType<IMethodSymbol>()
                .Single(static method => method.MethodKind == MethodKind.Ordinary);
        }

        var handlerClassFullName = handler.HandlerClassFullName ?? throw new InvalidOperationException("The handler execution contributor expected an external handler class name, but the current handler is inline.");
        var handlerType = context.FindTypeByMetadataName(handlerClassFullName)
            ?? throw new InvalidOperationException($"The handler execution contributor could not resolve external handler type '{handlerClassFullName}'.");

        return handlerType.GetMembers(handler.HandlerMethodName)
            .OfType<IMethodSymbol>()
            .Single(method => method.MethodKind == MethodKind.Ordinary
                && method.Parameters.Length > 0
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, context.Operation.TypeSymbol));
    }

    private static ITypeSymbol? GetEffectiveHandlerReturnType(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return null;
        }

        if (returnType is INamedTypeSymbol namedReturnType
            && namedReturnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && namedReturnType.Name == "Task")
        {
            return namedReturnType.TypeArguments.Length == 1
                ? namedReturnType.TypeArguments[0]
                : null;
        }

        return returnType;
    }

    private static bool TryValidateResultsUnionMembers(PipelineContributorContext context, IMethodSymbol handlerMethod, ImmutableArray<ITypeSymbol> memberTypes, INamedTypeSymbol operationResultType)
    {
        var isValid = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberType in memberTypes)
        {
            var displayName = memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if (!seen.Add(displayName))
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        DiagnosticDescriptors.DuplicateResultsUnionMember,
                        GetReturnTypeLocation(handlerMethod),
                        [displayName, handlerMethod.Name]));
                isValid = false;
                continue;
            }

            if (OperationResultContractHelper.IsSupportedResultsUnionMember(memberType, operationResultType) == false)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        DiagnosticDescriptors.UnsupportedResultsUnionMember,
                        GetReturnTypeLocation(handlerMethod),
                        [displayName, handlerMethod.Name]));
                isValid = false;
            }
        }

        return isValid;
    }

    private static bool IsNullablePlainValue(ITypeSymbol candidateType)
    {
        return candidateType.NullableAnnotation == NullableAnnotation.Annotated
            || candidateType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsCompatibleWithDeclaredContract(
        IMethodSymbol handlerMethod,
        ITypeSymbol normalizedResultType,
        ITypeSymbol? declaredContractType,
        INamedTypeSymbol noResultType,
        INamedTypeSymbol operationResultType,
        PipelineContributorContext context,
        bool allowMissingContract,
        ITypeSymbol? plainValueType = null)
    {
        if (declaredContractType is null)
        {
            if (allowMissingContract)
            {
                return true;
            }

            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.IncompatibleHandlerReturnContract,
                    GetReturnTypeLocation(handlerMethod),
                    [handlerMethod.Name, normalizedResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), "no response"]));
            return false;
        }

        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var declaredUnionMembers))
        {
            if (declaredUnionMembers.Any(member => SymbolEqualityComparer.Default.Equals(member, normalizedResultType)))
            {
                return true;
            }

            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.IncompatibleHandlerReturnContract,
                    GetReturnTypeLocation(handlerMethod),
                    [handlerMethod.Name, normalizedResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), declaredContractType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(normalizedResultType, noResultType)
            && SymbolEqualityComparer.Default.Equals(declaredContractType, noResultType) == false)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.IncompatibleHandlerReturnContract,
                    GetReturnTypeLocation(handlerMethod),
                    [handlerMethod.Name, normalizedResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), declaredContractType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(normalizedResultType, declaredContractType)
            || (plainValueType is not null && SymbolEqualityComparer.Default.Equals(plainValueType, declaredContractType)))
        {
            return true;
        }

        if (TryGetOkResultPayloadType(normalizedResultType, out var okResultPayloadType)
            && OperationResultContractHelper.IsOperationResultType(declaredContractType, operationResultType) == false
            && SymbolEqualityComparer.Default.Equals(okResultPayloadType, declaredContractType))
        {
            return true;
        }

        context.AddDiagnostic(
            new DiagnosticInfo(
                DiagnosticDescriptors.IncompatibleHandlerReturnContract,
                GetReturnTypeLocation(handlerMethod),
                [handlerMethod.Name, normalizedResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), declaredContractType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
        return false;
    }

    private static bool TryGetOkResultPayloadType(ITypeSymbol candidateType, [NotNullWhen(true)] out ITypeSymbol? payloadType)
    {
        if (candidateType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.ContainingNamespace.ToDisplayString() == "Blueprint.Runtime.Exec"
            && namedType.Name == "OkResult"
            && namedType.TypeArguments.Length == 1)
        {
            payloadType = namedType.TypeArguments[0];
            return true;
        }

        payloadType = null;
        return false;
    }

    private static LocationInfo? GetReturnTypeLocation(IMethodSymbol handlerMethod)
    {
        var syntaxReference = handlerMethod.DeclaringSyntaxReferences.FirstOrDefault();
        var syntax = syntaxReference?.GetSyntax();

        return syntax switch
        {
            MethodDeclarationSyntax method => LocationInfo.CreateFrom(method.ReturnType),
            _ when handlerMethod.Locations.Length > 0 => LocationInfo.CreateFrom(handlerMethod.Locations[0]),
            _ => null,
        };
    }

    private static bool IsBackgroundTask(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == "IBackgroundTask"
            && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Tasks");
    }

    private sealed record HandlerReturnNormalization(
        HandlerReturnNormalizationKind Kind,
        string ProducedTypeName,
        string? PlainValueTypeName);

    private enum HandlerReturnNormalizationKind
    {
        NoResult,
        PlainValue,
        OperationResult,
        ResultsUnion,
    }

    /// <summary>
    /// Scans handler method DI parameters and external handler constructor parameters for
    /// well-known singleton types (<c>ILogger&lt;T&gt;</c>, <c>ILoggerFactory</c>) and
    /// registers them as constructor-cached fields to eliminate per-request DI lookups.
    /// </summary>
    private static void CacheKnownSingletonServices(PipelineContributorContext context, OperationHandlerModel handler)
    {
        // Inline handler method parameters
        foreach (var param in handler.Parameters)
        {
            if (param.Source == ParameterSource.FromDependencyInjection)
            {
                TryCacheSingletonParameter(context, param.ParameterTypeName);
            }
        }

        // External handler constructor parameters
        if (handler.ConstructorParameters is { } ctorParams)
        {
            foreach (var param in ctorParams)
            {
                TryCacheSingletonParameter(context, param.ParameterTypeName);
            }
        }
    }

    private static void TryCacheSingletonParameter(PipelineContributorContext context, string parameterTypeName)
    {
        if (!IsKnownSingleton(parameterTypeName))
        {
            return;
        }

        var fieldName = ToFieldName(parameterTypeName);
        string resolutionExpression;

        if (parameterTypeName is "global::Microsoft.Extensions.Logging.ILoggerFactory"
            or "Microsoft.Extensions.Logging.ILoggerFactory")
        {
            // The pipeline constructor already receives loggerFactory as a parameter
            resolutionExpression = "loggerFactory";
        }
        else
        {
            // ILogger<T> → create from the loggerFactory parameter (works with NullLoggerFactory)
            resolutionExpression =
                $"Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<{ExtractGenericArgument(parameterTypeName)}>(loggerFactory)";
        }

        context.CacheService(fieldName, parameterTypeName, resolutionExpression);
    }

    /// <summary>
    /// Extracts the generic type argument from a type like <c>ILogger&lt;TestNamespace.Foo&gt;</c>.
    /// </summary>
    private static string ExtractGenericArgument(string genericTypeName)
    {
        var openAngle = genericTypeName.IndexOf('<');
        var closeAngle = genericTypeName.LastIndexOf('>');
        if (openAngle >= 0 && closeAngle > openAngle)
        {
            return genericTypeName.Substring(openAngle + 1, closeAngle - openAngle - 1);
        }

        return genericTypeName;
    }

    /// <summary>
    /// Returns <c>true</c> for types that the Microsoft.Extensions.DependencyInjection framework
    /// always registers as singletons, making them safe to resolve once in the pipeline constructor.
    /// </summary>
    private static bool IsKnownSingleton(string fullyQualifiedTypeName)
    {
        // ILoggerFactory is always a singleton
        if (fullyQualifiedTypeName is "global::Microsoft.Extensions.Logging.ILoggerFactory"
            or "Microsoft.Extensions.Logging.ILoggerFactory")
        {
            return true;
        }

        // ILogger<T> is always a singleton (Logger<T> is registered as singleton by AddLogging)
        if (fullyQualifiedTypeName.StartsWith("global::Microsoft.Extensions.Logging.ILogger<", StringComparison.Ordinal)
            || fullyQualifiedTypeName.StartsWith("Microsoft.Extensions.Logging.ILogger<", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a fully qualified type name into a safe field name for caching.
    /// </summary>
    private static string ToFieldName(string typeName)
    {
        // Strip all global:: prefixes (may appear in generic type arguments)
        var name = typeName.Replace("global::", string.Empty);

        // Extract the short type name for readability:
        // "Microsoft.Extensions.Logging.ILogger<TestNamespace.TestQuery>" → "ILogger_TestNamespace_TestQuery"
        var lastDot = name.LastIndexOf('.');
        var genericAngle = name.IndexOf('<');
        if (genericAngle >= 0 && (lastDot < 0 || lastDot > genericAngle))
        {
            lastDot = name.LastIndexOf('.', genericAngle);
        }

        var shortName = lastDot >= 0 ? name.Substring(lastDot + 1) : name;

        var sanitized = shortName
            .Replace('.', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .TrimEnd('_');

        return $"_cached_{sanitized}";
    }
}
