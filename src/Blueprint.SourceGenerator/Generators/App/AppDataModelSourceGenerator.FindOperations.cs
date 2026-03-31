using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.App;

/// <summary>
/// Incremental source generator that discovers Blueprint app scan declarations and emits app registrations.
/// </summary>
public sealed partial class AppDataModelSourceGenerator
{
    /// <summary>
    /// Given a <see cref="ParseOnlyDataModel"/> will scan for all operations that should
    /// be registered, based on the (possibly multiple) scan attributes.
    /// </summary>
    /// <param name="context">The context containing Compilation and the AppDataModel class to generate operation registrations for.</param>
    /// <param name="executionPlanResult">The shared descriptor-module execution plan used to inspect serializer requirements.</param>
    /// <param name="exceptionTranslatorPolicyResult">The shared exception-translation policy required by descriptor contributors.</param>
    /// <returns>A compilation model with all found operations.</returns>
    private static Result<BlueprintAppDataModelCompilationModel> FindOperations(
        (Result<ParseOnlyDataModel> Result, Compilation Compilation) context,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult)
    {
        var (diagnosticModel, compilation) = context;

        if (diagnosticModel.Value == null)
        {
            return Result<BlueprintAppDataModelCompilationModel>.Failure(diagnosticModel.Diagnostics);
        }

        var (dataModelClass, attributes) = diagnosticModel.Value;

        var registrations = new EquatableList<string>();
        var diagnostics = new EquatableList<DiagnosticInfo>();
        var missingSerializerCoverageTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var affectedSerializerCoverageOperations = new HashSet<string>(StringComparer.Ordinal);
        var coveredSerializerContextTypeNames = dataModelClass.SerializerContextTypeName is null
            ? null
            : GetCoveredSerializerContextTypeNames(dataModelClass.SerializerContextTypeName, compilation);

        foreach (var attribute in attributes)
        {
            var modelClassType = compilation.GetTypeByMetadataName(dataModelClass.MetadataName);

            foreach (var operation in ScanForOperationTypes(compilation, attribute, modelClassType!))
            {
                registrations.Add(operation.ToDisplayString());

                if (dataModelClass.SerializerContextTypeName is null)
                {
                    continue;
                }

                foreach (var missingTypeName in GetMissingSerializerContextCoverage(
                             operation,
                             compilation,
                             executionPlanResult,
                             exceptionTranslatorPolicyResult,
                             coveredSerializerContextTypeNames))
                {
                    missingSerializerCoverageTypeNames.Add(missingTypeName);
                    affectedSerializerCoverageOperations.Add(operation.Name);
                }
            }

            if (registrations.Count == 0)
            {
                return Result<BlueprintAppDataModelCompilationModel>.Failure(
                    new DiagnosticInfo(_noMatchingTypesFound, attribute.Location));
            }
        }

        if (dataModelClass.SerializerContextTypeName is not null
            && missingSerializerCoverageTypeNames.Count > 0)
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    _serializerContextMissingCoverageRule,
                    dataModelClass.SerializerContextLocation,
                    [
                        dataModelClass.SerializerContextTypeName.Replace("global::", string.Empty, StringComparison.Ordinal),
                        string.Join(", ", missingSerializerCoverageTypeNames.OrderBy(static typeName => typeName, StringComparer.Ordinal)),
                        string.Join(", ", affectedSerializerCoverageOperations.OrderBy(static operationName => operationName, StringComparer.Ordinal)),
                    ]));
        }

        return Result.Success(new BlueprintAppDataModelCompilationModel(dataModelClass, registrations, diagnostics));
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

    private static HashSet<string> GetCoveredSerializerContextTypeNames(
        string serializerContextTypeName,
        Compilation compilation)
    {
        var serializerContextType = compilation.GetTypeByMetadataName(NormalizeMetadataName(serializerContextTypeName));
        if (serializerContextType is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return serializerContextType.GetAttributes()
            .Where(static attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Text.Json.Serialization.JsonSerializableAttribute", StringComparison.Ordinal))
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<ITypeSymbol>()
            .Select(static typeSymbol => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string[] GetMissingSerializerContextCoverage(
        INamedTypeSymbol operation,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult,
        HashSet<string>? coveredSerializerContextTypeNames)
    {
        if (coveredSerializerContextTypeNames is null)
        {
            return [];
        }

        var plannedDescriptor = DescriptorPlanBuilder.Build(
            operation.ToFullMetadataName(),
            compilation,
            executionPlanResult,
            exceptionTranslatorPolicyResult);
        if (plannedDescriptor.Value is null)
        {
            return [];
        }

        var requiredBodyTypeNames = plannedDescriptor.Value.GetMetadata<SerializerContextCoverageRegistration>()
            .Select(static registration => registration.BodyTypeName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredBodyTypeNames.Length == 0)
        {
            return [];
        }

        return requiredBodyTypeNames
            .Where(requiredTypeName => !coveredSerializerContextTypeNames.Contains(requiredTypeName))
            .OrderBy(static typeName => typeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeMetadataName(string metadataName)
    {
        return metadataName.StartsWith("global::", StringComparison.Ordinal)
            ? metadataName.Substring("global::".Length)
            : metadataName;
    }
}


