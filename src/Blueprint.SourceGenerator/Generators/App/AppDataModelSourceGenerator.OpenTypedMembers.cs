using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.App;

/// <summary>
/// BP012 and BP013: members reachable from a declared response root whose static type does not say what they hold.
/// </summary>
/// <remarks>
/// <para>
/// A response root the app's <c>JsonSerializerContext</c> declares is written by that context alone. System.Text.Json's
/// own generator covers the root's static graph, but behind an <see cref="object" /> member - or a collection or
/// dictionary of them - a value is written as its runtime type, which the context knows only if it registers it, and
/// otherwise the response fails when it is written (Symprex/blueprint#94). Behind an interface or an abstract class
/// without <c>[JsonPolymorphic]</c>/<c>[JsonDerivedType]</c> a value is written as the declared type, silently dropping
/// whatever the runtime type adds. BP006 only checks that the root itself is declared, so this walks each declared root's
/// members, recursively through element, value and generic argument types and base classes, and reports each such member
/// once.
/// </para>
/// <para>
/// Only types whose shape the generator can see are walked: the compilation's own, the scanned assemblies' and
/// Blueprint's. A type from anywhere else (the BCL, NodaTime) is commonly written by a converter registered on the
/// options, which no attribute shows, so its public properties say nothing about its JSON.
/// </para>
/// </remarks>
public sealed partial class AppDataModelSourceGenerator
{
    private const string BlueprintRuntimeTypesAttributeMetadataName = "Blueprint.Model.BlueprintRuntimeTypesAttribute";

    private static readonly DiagnosticDescriptor _openTypedResponseMemberRule = new(
        id: "BP012",
        title: "Response body member is open-typed",
        messageFormat: "Member '{0}' of type '{1}', reachable from HTTP response body type '{2}', can hold runtime types the app-owned serializer context '{3}' cannot see at build time, so a value of a type it does not register fails the response when it is written. Declare the types it holds with [BlueprintRuntimeTypes(typeof(...))], give it a [JsonConverter], or set AllowRuntimeTypeFallback = true on the app's [BlueprintAppSerializerContext].",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A declared response root is written by the app-owned source-generated JsonSerializerContext alone. Behind an object member, or a collection or dictionary of them, a value is written as its runtime type, which the context cannot see at build time; behind an interface or abstract class without [JsonPolymorphic]/[JsonDerivedType] it is written as the declared type, dropping the rest. The member must say what it holds.");

    private static readonly DiagnosticDescriptor _unregisteredRuntimeTypeRule = new(
        id: "BP013",
        title: "Declared runtime type is not registered with the serializer context",
        messageFormat: "Member '{0}' declares runtime type '{1}' with [BlueprintRuntimeTypes], but the app-owned serializer context '{2}' does not register it. Add [JsonSerializable(typeof({1}))] to the context.",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[BlueprintRuntimeTypes] clears BP012 by declaring the runtime types an open-typed response member holds, which the app-owned serializer context must then register so that writing them does not fail.");

    private static IEnumerable<DiagnosticInfo> GetOpenTypedResponseMemberDiagnostics(
        Compilation compilation,
        ParseOnlyBlueprintAppClassModel dataModelClass,
        HashSet<string> registeredTypeNames,
        IEnumerable<string> coveredRequiredTypeNames,
        HashSet<IAssemblySymbol> scannedAssemblies)
    {
        var contextName = dataModelClass.SerializerContextTypeName!.Replace("global::", string.Empty, StringComparison.Ordinal);
        if (compilation.GetTypeByMetadataName(contextName) is not { } contextType)
        {
            yield break;
        }

        var required = new HashSet<string>(coveredRequiredTypeNames, StringComparer.Ordinal);
        var roots = contextType.GetAttributes()
            .Where(static attribute => attribute.AttributeClass is { Name: "JsonSerializableAttribute" } ac && ac.IsInNamespace("System", "Text", "Json", "Serialization"))
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<ITypeSymbol>()
            .Select(static type => (Type: type, Name: GetRegisteredTypeName(type)))
            .Where(root => required.Contains(root.Name))
            .GroupBy(static root => root.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static root => root.Name, StringComparer.Ordinal);

        var walker = new OpenTypedMemberWalker(compilation, dataModelClass, contextName, registeredTypeNames, scannedAssemblies);
        foreach (var (type, name) in roots)
        {
            walker.Walk(type, name);
        }

        foreach (var diagnostic in walker.Diagnostics)
        {
            yield return diagnostic;
        }
    }

    private static string GetRegisteredTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private sealed class OpenTypedMemberWalker(
        Compilation compilation,
        ParseOnlyBlueprintAppClassModel dataModelClass,
        string contextName,
        HashSet<string> registeredTypeNames,
        HashSet<IAssemblySymbol> scannedAssemblies)
    {
        // Blueprint's own members that are open by design and written through reflection at runtime
        // (SerializerContextTypeInfoCache): a problem's extensions and a resource event's payload and bags.
        private static readonly HashSet<string> _openByDesignMembers = new(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions",
            "Blueprint.Http.ResourceEvent.Data",
            "Blueprint.Http.ResourceEvent.ChangedValues",
            "Blueprint.Http.ResourceEvent.Metadata",
            "Blueprint.Http.ResourceEvent.SecureData",
        };

        private readonly HashSet<ITypeSymbol> _visited = new(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private readonly INamedTypeSymbol? _enumerableOfT = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        private readonly INamedTypeSymbol? _dictionaryOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        private readonly INamedTypeSymbol? _readOnlyDictionaryOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");

        public List<DiagnosticInfo> Diagnostics { get; } = [];

        public void Walk(ITypeSymbol rootType, string rootName)
        {
            this.VisitType(rootType, null, rootName);
        }

        private void VisitType(ITypeSymbol type, IPropertySymbol? member, string rootName)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (HasAttribute(type, "JsonConverterAttribute"))
            {
                return;
            }

            if (this.IsOpen(type))
            {
                this.ReportOpen(member, rootName);
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                this.VisitType(array.ElementType, member, rootName);
                return;
            }

            if (type.SpecialType == SpecialType.System_String || type is not INamedTypeSymbol named)
            {
                return;
            }

            if (this.TryGetDictionaryValueType(named, out var valueType))
            {
                this.VisitType(valueType, member, rootName);
                return;
            }

            if (this.TryGetEnumerableElementType(named, out var elementType))
            {
                this.VisitType(elementType, member, rootName);
                return;
            }

            if (named.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable)
                || named.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                // A non-generic collection's elements are objects.
                this.ReportOpen(member, rootName);
                return;
            }

            if (named.IsGenericType && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>")
            {
                this.VisitType(named.TypeArguments[1], member, rootName);
                return;
            }

            if (!this.IsWalkable(named) || !this._visited.Add(named))
            {
                return;
            }

            foreach (var property in GetSerializedProperties(named))
            {
                this.VisitMember(property, rootName);
            }

            foreach (var derived in GetDerivedTypes(named))
            {
                this.VisitType(derived, member, rootName);
            }
        }

        private void VisitMember(IPropertySymbol property, string rootName)
        {
            if (HasAttribute(property, "JsonConverterAttribute")
                || _openByDesignMembers.Contains(GetMemberName(property.OriginalDefinition)))
            {
                return;
            }

            var runtimeTypes = property.GetAttributes()
                .FirstOrDefault(static attribute => attribute.AttributeClass?.ToFullMetadataName() == BlueprintRuntimeTypesAttributeMetadataName);

            if (runtimeTypes is null)
            {
                this.VisitType(property.Type, property, rootName);
                return;
            }

            foreach (var runtimeType in GetRuntimeTypes(runtimeTypes))
            {
                var runtimeTypeName = GetRegisteredTypeName(runtimeType);
                if (!registeredTypeNames.Contains(runtimeTypeName))
                {
                    this.Diagnostics.Add(new DiagnosticInfo(
                        _unregisteredRuntimeTypeRule,
                        this.GetLocation(runtimeTypes, property),
                        [GetMemberName(property), runtimeTypeName, contextName]));
                }

                // A declared runtime type is written as itself, so what it reaches is reached from the root too.
                this.VisitType(runtimeType, null, rootName);
            }
        }

        private void ReportOpen(IPropertySymbol? member, string rootName)
        {
            // An open root itself is not this diagnostic's: BP006 asks only that the root be declared.
            if (member is null || !this._reported.Add(GetMemberName(member)))
            {
                return;
            }

            this.Diagnostics.Add(new DiagnosticInfo(
                _openTypedResponseMemberRule,
                this.GetLocation(null, member),
                [GetMemberName(member), member.Type.ToDisplayString(), rootName, contextName]));
        }

        private bool IsOpen(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            // System.Text.Json's own document types are self-describing and written by their own converters.
            if (type.ContainingNamespace?.ToDisplayString() is "System.Text.Json" or "System.Text.Json.Nodes")
            {
                return false;
            }

            if (type.TypeKind == TypeKind.Interface)
            {
                return !this.IsCollection(type) && !IsPolymorphic(type);
            }

            return type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true } abstractType
                && !this.IsCollection(abstractType)
                && !IsPolymorphic(abstractType);
        }

        private bool IsCollection(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_Collections_IEnumerable
                || type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable)
                || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, this._enumerableOfT);
        }

        private bool TryGetDictionaryValueType(INamedTypeSymbol type, out ITypeSymbol valueType)
        {
            foreach (var candidate in type.AllInterfaces.Prepend(type))
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, this._dictionaryOfT)
                    || SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, this._readOnlyDictionaryOfT))
                {
                    valueType = candidate.TypeArguments[1];
                    return true;
                }
            }

            valueType = null!;
            return false;
        }

        private bool TryGetEnumerableElementType(INamedTypeSymbol type, out ITypeSymbol elementType)
        {
            foreach (var candidate in type.AllInterfaces.Prepend(type))
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, this._enumerableOfT))
                {
                    elementType = candidate.TypeArguments[0];
                    return true;
                }
            }

            elementType = null!;
            return false;
        }

        private bool IsWalkable(INamedTypeSymbol type)
        {
            var assembly = type.ContainingAssembly;
            if (assembly is null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                || scannedAssemblies.Contains(assembly)
                || assembly.Name.StartsWith("Blueprint.", StringComparison.Ordinal);
        }

        private LocationInfo? GetLocation(AttributeData? attribute, IPropertySymbol member)
        {
            if (attribute?.ApplicationSyntaxReference is { } attributeSyntax)
            {
                return LocationInfo.CreateFrom(attributeSyntax.GetSyntax());
            }

            var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
            return location is null ? dataModelClass.SerializerContextLocation : LocationInfo.CreateFrom(location);
        }

        private static IEnumerable<IPropertySymbol> GetSerializedProperties(INamedTypeSymbol type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Most-derived first, so a property that hides or overrides one on a base class is the one walked.
            foreach (var property in type.GetAllMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || !seen.Add(property.Name))
                {
                    continue;
                }

                var included = HasAttribute(property, "JsonIncludeAttribute");
                if (property.GetMethod is null
                    || (!included && (property.DeclaredAccessibility != Accessibility.Public || property.GetMethod.DeclaredAccessibility != Accessibility.Public))
                    || IsAlwaysIgnored(property))
                {
                    continue;
                }

                yield return property;
            }
        }

        private static bool IsAlwaysIgnored(IPropertySymbol property)
        {
            var ignore = property.GetAttributes().FirstOrDefault(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonIgnoreAttribute"));
            if (ignore is null)
            {
                return false;
            }

            // JsonIgnoreCondition.Always is 1 and the default; the others still write the member.
            var condition = ignore.NamedArguments.FirstOrDefault(static argument => argument.Key == "Condition").Value;
            return condition.Value is null or 1;
        }

        private static IEnumerable<ITypeSymbol> GetDerivedTypes(INamedTypeSymbol type)
        {
            return type.GetAttributes()
                .Where(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonDerivedTypeAttribute"))
                .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
                .OfType<ITypeSymbol>();
        }

        private static IEnumerable<ITypeSymbol> GetRuntimeTypes(AttributeData attribute)
        {
            return attribute.ConstructorArguments
                .SelectMany(static argument => argument.Kind == TypedConstantKind.Array ? argument.Values : [argument])
                .Select(static argument => argument.Value)
                .OfType<ITypeSymbol>();
        }

        private static bool IsPolymorphic(ITypeSymbol type)
        {
            return HasAttribute(type, "JsonPolymorphicAttribute") || HasAttribute(type, "JsonDerivedTypeAttribute");
        }

        private static bool HasAttribute(ISymbol symbol, string name)
        {
            return symbol.GetAttributes().Any(attribute => IsJsonAttribute(attribute.AttributeClass, name));
        }

        private static bool IsJsonAttribute(INamedTypeSymbol? attributeClass, string name)
        {
            return attributeClass is { } ac && ac.Name == name && ac.IsInNamespace("System", "Text", "Json", "Serialization");
        }

        private static string GetMemberName(IPropertySymbol property)
        {
            return property.ContainingType.ToDisplayString() + "." + property.Name;
        }
    }
}
