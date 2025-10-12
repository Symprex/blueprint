using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.App;

public sealed partial class AppDataModelSourceGenerator
{
    /// <summary>
    /// Given a <see cref="ParseOnlyDataModel"/> will scan for all operations that should
    /// be registered, based on the (possibly multiple) scan attributes.
    /// </summary>
    /// <param name="context">The context containing Compilation and the AppDataModel class to generate operation registrations for.</param>
    /// <returns>A compilation model with all found operations.</returns>
    private static Result<BlueprintAppDataModelCompilationModel> FindOperations(
        (Result<ParseOnlyDataModel> Result, Compilation Compilation) context)
    {
        var (diagnosticModel, compilation) = context;

        if (diagnosticModel.Value == null)
        {
            return Result<BlueprintAppDataModelCompilationModel>.Failure(diagnosticModel.Diagnostics);
        }

        var (dataModelClass, attributes) = diagnosticModel.Value;

        var registrations = new EquatableList<string>();

        foreach (var attribute in attributes)
        {
            var modelClassType = compilation.GetTypeByMetadataName(dataModelClass.MetadataName);

            foreach (var operation in ScanForOperationTypes(compilation, attribute, modelClassType!))
            {
                registrations.Add(operation.ToDisplayString());
            }

            if (registrations.Count == 0)
            {
                return Result<BlueprintAppDataModelCompilationModel>.Failure(
                    new DiagnosticInfo(_noMatchingTypesFound, attribute.Location));
            }
        }

        return Result.Success(new BlueprintAppDataModelCompilationModel(dataModelClass, registrations));
    }

    /// <summary>
    /// Given a <see cref="ParseOnlyOperationScanAttributeModel"/> will scan for all operations that should
    /// be registered.
    /// </summary>
    /// <param name="compilation">The compilation, used to scan for assemblies.</param>
    /// <param name="parseOnlyOperationScanAttribute">The scan attribute model.</param>
    /// <param name="modelClassType">The BlueprintAppDataModel class we are generating registrations for.</param>
    /// <returns>All found operation types.</returns>
    private static IEnumerable<INamedTypeSymbol> ScanForOperationTypes(
        Compilation compilation,
        ParseOnlyOperationScanAttributeModel parseOnlyOperationScanAttribute,
        INamedTypeSymbol modelClassType)
    {
        var assembly = (parseOnlyOperationScanAttribute.AssemblyOfTypeName is null
            ? modelClassType
            : compilation.GetTypeByMetadataName(parseOnlyOperationScanAttribute.AssemblyOfTypeName))?.ContainingAssembly;

        var assignableToType = compilation.GetTypeByMetadataName("Blueprint.Model.IOperation");

        if (assignableToType == null)
        {
            yield break;
        }

        foreach (var type in GetTypesFromAssembly(assembly))
        {
            if (type.IsAbstract || type.IsStatic || !type.CanBeReferencedByName || type.TypeKind != TypeKind.Class || type.IsGenericType)
            {
                continue;
            }

            if (!IsAssignableTo(type, assignableToType))
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


