using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Incremental source generator that discovers operation handlers and generates executor pipelines.
/// </summary>
public partial class OperationExecutorPipelineSourceGenerator
{
    /// <summary>
    /// Finds handlers for all operations in the BlueprintApp.
    /// </summary>
    private static Result<PipelineCompilationModel> FindHandlersForOperations(
        (Result<AppInfo> AppResult, Compilation Compilation) context)
    {
        var (appResult, compilation) = context;

        if (appResult.Value == null)
        {
            return Result<PipelineCompilationModel>.Failure(appResult.Diagnostics);
        }

        var appInfo = appResult.Value;
        var diagnostics = new List<DiagnosticInfo>();
        var handlers = new List<OperationHandlerModel>();

        var operationTypes = ScanForOperationTypes(compilation, appInfo.AppType);

        foreach (var operationType in operationTypes)
        {
            // Find handler for this operation
            var handlerResult = HandlerDiscovery.FindHandler(operationType, compilation);

            if (handlerResult.Diagnostics.Count > 0)
            {
                diagnostics.AddRange(handlerResult.Diagnostics);
            }

            if (handlerResult.Value != null)
            {
                handlers.Add(handlerResult.Value);
            }
        }

        // If we have diagnostics but no handlers, it's a failure
        if (diagnostics.Count > 0 && handlers.Count == 0)
        {
            return Result<PipelineCompilationModel>.Failure(new EquatableList<DiagnosticInfo>(diagnostics.ToArray()));
        }

        var model = new PipelineCompilationModel(
            appInfo.AppTypeName,
            appInfo.AppNamespace,
            new EquatableList<OperationHandlerModel>(handlers.ToArray()));

        // Return success with any warnings
        return diagnostics.Count > 0
            ? new Result<PipelineCompilationModel>(model, new EquatableList<DiagnosticInfo>(diagnostics.ToArray()))
            : Result.Success(model);
    }

    /// <summary>
    /// Scans for all operation types selected by the app's <c>[BlueprintAppScan]</c> declarations.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ScanForOperationTypes(
        Compilation compilation,
        INamedTypeSymbol appType)
    {
        var operationInterface = compilation.GetTypeByMetadataName("Blueprint.Model.IOperation");

        if (operationInterface == null)
        {
            yield break;
        }

        var discoveredTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var scanAttributes = appType.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "Blueprint.Model.BlueprintAppScanAttribute")
            .Select(ParseOnlyOperationScanAttributeModel.Create);

        foreach (var scanAttribute in scanAttributes)
        {
            var assembly = (scanAttribute.AssemblyOfTypeName is null
                ? appType
                : compilation.GetTypeByMetadataName(scanAttribute.AssemblyOfTypeName))?.ContainingAssembly;

            foreach (var type in GetTypesFromAssembly(assembly))
            {
                if (type.IsAbstract || type.IsStatic || !type.CanBeReferencedByName ||
                    type.TypeKind != TypeKind.Class || type.IsGenericType)
                {
                    continue;
                }

                if (!IsAssignableTo(type, operationInterface))
                {
                    continue;
                }

                if (discoveredTypes.Add(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static bool IsAssignableTo(INamedTypeSymbol type, INamedTypeSymbol assignableTo)
    {
        if (SymbolEqualityComparer.Default.Equals(type, assignableTo))
        {
            return true;
        }

        if (assignableTo.TypeKind == TypeKind.Interface)
        {
            return type.AllInterfaces.Contains(assignableTo, SymbolEqualityComparer.Default);
        }

        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, assignableTo))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetTypesFromAssembly(IAssemblySymbol? assemblySymbol)
    {
        if (assemblySymbol == null)
        {
            return [];
        }

        var @namespace = assemblySymbol.GlobalNamespace;
        return GetTypesFromNamespaceOrType(@namespace);

        static IEnumerable<INamedTypeSymbol> GetTypesFromNamespaceOrType(INamespaceOrTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (member is not INamespaceOrTypeSymbol namespaceOrType)
                {
                    continue;
                }

                if (member is INamedTypeSymbol namedType)
                {
                    yield return namedType;
                }

                foreach (var type in GetTypesFromNamespaceOrType(namespaceOrType))
                {
                    yield return type;
                }
            }
        }
    }
}
