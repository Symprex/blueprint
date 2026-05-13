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
        var missingSerializerCoverageOperations = new HashSet<string>(StringComparer.Ordinal);
        var requiredSerializerCoverageTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var requiredSerializerCoverageOperations = new HashSet<string>(StringComparer.Ordinal);
        var strippedNameToOperations = new Dictionary<string, List<string>>(128, StringComparer.Ordinal);
        var coveredSerializerContextTypeNames = dataModelClass.SerializerContextTypeName is null
            ? null
            : new HashSet<string>(dataModelClass.SerializerContextBodyTypeNames.AsArray(), StringComparer.Ordinal);

        // Cache the model class type outside the attribute loop — metadata name is constant.
        var modelClassType = compilation.GetTypeByMetadataName(dataModelClass.MetadataName);

        foreach (var attribute in attributes)
        {
            foreach (var operation in ScanForOperationTypes(compilation, attribute, modelClassType!))
            {
                registrations.Add(operation.ToDisplayString());

                var strippedName = OperationNameHelpers.StripOperationSuffix(operation.Name);
                if (!strippedNameToOperations.TryGetValue(strippedName, out var operationNames))
                {
                    operationNames = new List<string>();
                    strippedNameToOperations[strippedName] = operationNames;
                }

                operationNames.Add(operation.Name);

                var requiredTypeNames = GetRequiredSerializerContextCoverage(
                    operation,
                    compilation,
                    executionPlanResult,
                    exceptionTranslatorPolicyResult);
                if (requiredTypeNames.Length == 0)
                {
                    continue;
                }

                foreach (var requiredTypeName in requiredTypeNames)
                {
                    requiredSerializerCoverageTypeNames.Add(requiredTypeName);
                    requiredSerializerCoverageOperations.Add(operation.Name);
                }

                if (coveredSerializerContextTypeNames is null)
                {
                    continue;
                }

                foreach (var requiredTypeName in requiredTypeNames)
                {
                    if (!coveredSerializerContextTypeNames.Contains(requiredTypeName))
                    {
                        missingSerializerCoverageTypeNames.Add(requiredTypeName);
                        missingSerializerCoverageOperations.Add(operation.Name);
                    }
                }
            }

            if (registrations.Count == 0)
            {
                return Result<BlueprintAppDataModelCompilationModel>.Failure(
                    new DiagnosticInfo(_noMatchingTypesFound, attribute.Location));
            }
        }

        foreach (var kvp in strippedNameToOperations)
        {
            if (kvp.Value.Count > 1)
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        _duplicateStrippedDescriptorNameRule,
                        dataModelClass.DeclarationLocation,
                        [
                            string.Join(", ", kvp.Value),
                            kvp.Key,
                        ]));
            }
        }

        if (dataModelClass.SerializerContextTypeName is null
            && requiredSerializerCoverageTypeNames.Count > 0)
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    _missingSerializerContextDeclarationRule,
                    dataModelClass.DeclarationLocation,
                    [
                        dataModelClass.MetadataName.Replace("global::", string.Empty, StringComparison.Ordinal),
                        string.Join(", ", requiredSerializerCoverageTypeNames.OrderBy(static typeName => typeName, StringComparer.Ordinal)),
                        string.Join(", ", requiredSerializerCoverageOperations.OrderBy(static operationName => operationName, StringComparer.Ordinal)),
                    ]));
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
                        string.Join(", ", missingSerializerCoverageOperations.OrderBy(static operationName => operationName, StringComparer.Ordinal)),
                    ]));
        }

        if (dataModelClass.SerializerContextTypeName is not null
            && coveredSerializerContextTypeNames is not null)
        {
            foreach (var diagnostic in GetUnsupportedOpenApiValidationDiagnostics(
                         compilation,
                         dataModelClass,
                         requiredSerializerCoverageTypeNames.Where(coveredSerializerContextTypeNames.Contains)))
            {
                diagnostics.Add(diagnostic);
            }
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

    private static string[] GetRequiredSerializerContextCoverage(
        INamedTypeSymbol operation,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult)
    {
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
            .OrderBy(static typeName => typeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<DiagnosticInfo> GetUnsupportedOpenApiValidationDiagnostics(
        Compilation compilation,
        ParseOnlyBlueprintAppClassModel dataModelClass,
        IEnumerable<string> coveredRequiredTypeNames)
    {
        foreach (var typeName in coveredRequiredTypeNames.Distinct(StringComparer.Ordinal))
        {
            if (compilation.GetTypeByMetadataName(typeName) is not INamedTypeSymbol bodyType)
            {
                continue;
            }

            foreach (var property in bodyType.GetAllMembers().OfType<IPropertySymbol>())
            {
                foreach (var attribute in property.GetAttributes())
                {
                    if (ValidationSymbolHelpers.IsCompareAttribute(attribute.AttributeClass) == false
                        && ValidationSymbolHelpers.IsGreaterThanPropertyAttribute(attribute.AttributeClass) == false
                        && ValidationSymbolHelpers.IsLessThanPropertyAttribute(attribute.AttributeClass) == false)
                    {
                        continue;
                    }

                    yield return new DiagnosticInfo(
                        _unsupportedOpenApiValidationShapeRule,
                        ValidationSymbolHelpers.GetReferencedPropertyLocation(attribute, property) ?? dataModelClass.SerializerContextLocation,
                        [
                            dataModelClass.SerializerContextTypeName!.Replace("global::", string.Empty, StringComparison.Ordinal),
                            typeName,
                            property.Name,
                            attribute.AttributeClass!.Name,
                        ]);
                }
            }
        }
    }
}


