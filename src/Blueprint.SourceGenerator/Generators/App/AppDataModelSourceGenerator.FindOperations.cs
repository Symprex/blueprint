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

        // Track (route, HTTP method) → first operation to detect cross-operation route collisions.
        // Key is the (route template, HTTP method) pair; value is (operation FQN, operation name, attribute location).
        var routeMethodOwners = new Dictionary<(string Route, string Method), (string OperationFqn, string OperationName, LocationInfo? Location)>(128);

        // Track operations by FQN to avoid false collision reports from overlapping scans.
        var seenOperationFqns = new HashSet<string>(StringComparer.Ordinal);
        var coveredSerializerContextTypeNames = dataModelClass.SerializerContextTypeName is null
            ? null
            : new HashSet<string>(dataModelClass.SerializerContextBodyTypeNames.AsArray(), StringComparer.Ordinal);

        // Cache the model class type outside the attribute loop — metadata name is constant.
        var modelClassType = compilation.GetTypeByMetadataName(dataModelClass.MetadataName);

        // Collect the distinct assemblies that [BlueprintAppScan] targets so the self-link
        // cache only considers operations owned by this app, not transitively-referenced ones.
        var scannedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in attributes)
        {
            var assembly = (attribute.AssemblyOfTypeName is null
                ? modelClassType
                : compilation.GetTypeByMetadataName(attribute.AssemblyOfTypeName))?.ContainingAssembly;
            if (assembly is not null)
            {
                scannedAssemblies.Add(assembly);
            }
        }

        // Share a single CompilationOperationLookup across all operations so the self-link
        // cache is built once instead of N times (significant at S365 scale: ~540 operations).
        var sharedOperationLookup = new CompilationOperationLookup(compilation, scannedAssemblies);

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

                // Cross-operation route collision detection: collect (route, method) pairs and
                // flag when a second operation claims the same combination.
                var operationFqn = operation.ToFullMetadataName();
                if (seenOperationFqns.Add(operationFqn))
                {
                    var routeTemplates = HttpHostExposureHelper.GetAllRouteTemplatesWithLocations(operation);
                    if (routeTemplates.Count > 0)
                    {
                        var httpMethods = HttpHostExposureHelper.ResolveHttpMethods(operation);

                        foreach (var (template, templateLocation) in routeTemplates)
                        {
                            foreach (var method in httpMethods)
                            {
                                var routeKey = (template, method);

                                if (routeMethodOwners.TryGetValue(routeKey, out var existing)
                                    && !string.Equals(existing.OperationFqn, operationFqn, StringComparison.Ordinal))
                                {
                                    diagnostics.Add(
                                        new DiagnosticInfo(
                                            _crossOperationRouteCollisionRule,
                                            templateLocation ?? dataModelClass.DeclarationLocation,
                                            [
                                                existing.OperationName,
                                                operation.Name,
                                                template,
                                                method,
                                            ]));
                                }
                                else
                                {
                                    routeMethodOwners[routeKey] = (operationFqn, operation.Name, templateLocation);
                                }
                            }
                        }
                    }
                }

                var requiredTypeNames = GetRequiredSerializerContextCoverage(
                    operation,
                    compilation,
                    executionPlanResult,
                    exceptionTranslatorPolicyResult,
                    sharedOperationLookup);
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

        // Only scan for link checkers and api authorisers when the compilation references Blueprint.Runtime (has BlueprintServiceBuilder)
        var hasServiceBuilder = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder") is not null;
        var linkCheckerTypes = hasServiceBuilder
            ? ScanForLinkCheckerTypes(compilation, scannedAssemblies)
            : new EquatableList<LinkCheckerRegistrationInfo>([]);
        var apiAuthoriserTypes = hasServiceBuilder
            ? ScanForApiAuthoriserTypes(compilation, scannedAssemblies)
            : new EquatableList<string>([]);

        return Result.Success(new BlueprintAppDataModelCompilationModel(
            dataModelClass,
            registrations,
            linkCheckerTypes,
            apiAuthoriserTypes,
            hasServiceBuilder,
            diagnostics));
    }

    /// <summary>
    /// Scans all assemblies targeted by <c>[BlueprintAppScan]</c> plus the declaring assembly for concrete classes
    /// that inherit from <c>StateBasedLinkChecker&lt;TOperation, TResource&gt;</c>. Returns registration info
    /// including both the checker type name and the resource type it targets.
    /// </summary>
    private static EquatableList<LinkCheckerRegistrationInfo> ScanForLinkCheckerTypes(
        Compilation compilation,
        HashSet<IAssemblySymbol> scannedAssemblies)
    {
        var baseType = compilation.GetTypeByMetadataName("Blueprint.Authorization.StateBasedLinkChecker`2");

        if (baseType is null)
        {
            return new EquatableList<LinkCheckerRegistrationInfo>([]);
        }

        var results = new List<LinkCheckerRegistrationInfo>();

        // Include the compilation's own assembly in addition to the [BlueprintAppScan]-targeted assemblies,
        // since link checkers typically live in the host project alongside the app class.
        var allAssemblies = new HashSet<IAssemblySymbol>(scannedAssemblies, SymbolEqualityComparer.Default)
        {
            compilation.Assembly,
        };

        foreach (var assembly in allAssemblies)
        {
            foreach (var type in GetTypesFromAssembly(assembly))
            {
                if (type.IsAbstract || type.IsStatic || !type.CanBeReferencedByName || type.TypeKind != TypeKind.Class || type.IsGenericType)
                {
                    continue;
                }

                // Skip types that are not externally accessible (nested private, internal nested in private, etc.)
                if (!IsExternallyAccessible(type))
                {
                    continue;
                }

                var resourceType = GetGenericBaseResourceType(type, baseType);
                if (resourceType is not null)
                {
                    results.Add(new LinkCheckerRegistrationInfo(
                        type.ToFullMetadataName(),
                        resourceType.ToFullMetadataName()));
                }
            }
        }

        results.Sort(static (a, b) => string.Compare(a.CheckerTypeName, b.CheckerTypeName, StringComparison.Ordinal));
        return new EquatableList<LinkCheckerRegistrationInfo>([.. results]);
    }

    /// <summary>
    /// Scans all referenced assemblies (not just <c>[BlueprintAppScan]</c> targets) for concrete classes
    /// that implement <c>IApiAuthoriser</c>. This is broader than operation scanning because authorisers
    /// are cross-cutting infrastructure that often lives in shared assemblies (e.g. CommonCoreAssembly).
    /// </summary>
    private static EquatableList<string> ScanForApiAuthoriserTypes(
        Compilation compilation,
        HashSet<IAssemblySymbol> scannedAssemblies)
    {
        var interfaceType = compilation.GetTypeByMetadataName("Blueprint.Authorization.IApiAuthoriser");

        if (interfaceType is null)
        {
            return new EquatableList<string>([]);
        }

        var results = new List<string>();

        // Scan BlueprintAppScan assemblies + compilation assembly + all referenced assemblies
        // because authorisers are cross-cutting and may live in shared infrastructure assemblies.
        var allAssemblies = new HashSet<IAssemblySymbol>(scannedAssemblies, SymbolEqualityComparer.Default)
        {
            compilation.Assembly,
        };

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol refAssembly)
            {
                allAssemblies.Add(refAssembly);
            }
        }

        foreach (var assembly in allAssemblies)
        {
            // Skip framework/BCL assemblies for performance
            var name = assembly.Name;
            if (name.StartsWith("System", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                name.StartsWith("netstandard", StringComparison.Ordinal) ||
                name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                name.StartsWith("Blueprint.", StringComparison.Ordinal) ||
                name == "Blueprint")
            {
                continue;
            }

            foreach (var type in GetTypesFromAssembly(assembly))
            {
                if (type.IsAbstract || type.IsStatic || !type.CanBeReferencedByName || type.TypeKind != TypeKind.Class || type.IsGenericType)
                {
                    continue;
                }

                if (!IsExternallyAccessible(type))
                {
                    continue;
                }

                if (type.IsAssignableTo(interfaceType))
                {
                    results.Add(type.ToFullMetadataName());
                }
            }
        }

        results.Sort(StringComparer.Ordinal);
        return new EquatableList<string>([.. results]);
    }

    /// <summary>
    /// Extracts the TResource type argument (second type parameter) from the
    /// <c>StateBasedLinkChecker&lt;TOperation, TResource&gt;</c> base class of the given type.
    /// Returns null if the type does not inherit from the base.
    /// </summary>
    private static INamedTypeSymbol? GetGenericBaseResourceType(INamedTypeSymbol type, INamedTypeSymbol openGenericBase)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, openGenericBase))
            {
                // TResource is the second type argument of StateBasedLinkChecker<TOperation, TResource>
                return current.TypeArguments.Length >= 2 ? current.TypeArguments[1] as INamedTypeSymbol : null;
            }

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a type is externally accessible — i.e. it and all containing types
    /// have public or internal accessibility (not private or protected).
    /// </summary>
    private static bool IsExternallyAccessible(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
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

            if (!type.IsAssignableTo(assignableToType))
            {
                continue;
            }

            yield return type;
        }
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
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult,
        CompilationOperationLookup sharedOperationLookup)
    {
        var plannedDescriptor = DescriptorPlanBuilder.Build(
            operation.ToFullMetadataName(),
            compilation,
            executionPlanResult,
            exceptionTranslatorPolicyResult,
            sharedOperationLookup);
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


