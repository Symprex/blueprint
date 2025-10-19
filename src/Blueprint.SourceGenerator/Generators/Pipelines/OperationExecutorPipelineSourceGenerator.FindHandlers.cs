using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

public sealed partial class OperationExecutorPipelineSourceGenerator
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

        // Get all operation types by scanning the assembly
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
        if (diagnostics.Count > 0)
        {
            return new Result<PipelineCompilationModel>(model, new EquatableList<DiagnosticInfo>(diagnostics.ToArray()));
        }

        return Result.Success(model);
    }

    /// <summary>
    /// Scans for all operation types in the same assembly as the app.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ScanForOperationTypes(
        Compilation compilation,
        INamedTypeSymbol appType)
    {
        var assembly = appType.ContainingAssembly;
        var operationInterface = compilation.GetTypeByMetadataName("Blueprint.Model.IOperation");

        if (operationInterface == null)
        {
            yield break;
        }

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

            yield return type;
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

    private static IEnumerable<INamedTypeSymbol> GetTypesFromAssembly(IAssemblySymbol assemblySymbol)
    {
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
