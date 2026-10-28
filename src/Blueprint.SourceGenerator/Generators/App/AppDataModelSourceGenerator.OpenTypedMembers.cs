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
        if (ResolveSerializerContext(compilation, dataModelClass, contextName) is not { } contextType)
        {
            yield break;
        }

        var required = new HashSet<string>(coveredRequiredTypeNames, StringComparer.Ordinal);
        var roots = contextType.GetAttributes()
            .Where(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonSerializableAttribute"))
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<ITypeSymbol>()
            .Select(static type => (Type: type, Name: GetRegisteredTypeName(type)))
            .Where(root => required.Contains(root.Name))
            .GroupBy(static root => root.Name, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static root => root.Name, StringComparer.Ordinal);

        var walker = new OpenTypedMemberWalker(
            compilation,
            dataModelClass,
            contextName,
            ContextMemberOptions.From(contextType),
            registeredTypeNames,
            scannedAssemblies);

        foreach (var (type, name) in roots)
        {
            walker.Walk(type, name);
        }

        foreach (var diagnostic in walker.Diagnostics)
        {
            yield return diagnostic;
        }
    }

    /// <summary>
    /// Resolves the context from the app's own <c>[BlueprintAppSerializerContext]</c>, because the model holds only its
    /// display name, which is not a metadata name for a nested context (<c>Outer.AppJsonContext</c> against
    /// <c>Outer+AppJsonContext</c>).
    /// </summary>
    private static INamedTypeSymbol? ResolveSerializerContext(Compilation compilation, ParseOnlyBlueprintAppClassModel dataModelClass, string contextName)
    {
        var declared = compilation.GetTypeByMetadataName(dataModelClass.MetadataName)?
            .GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.ToFullMetadataName() == SerializerContextAttributeMetadataName)
            .SelectMany(static attribute => attribute.NamedArguments)
            .Where(static argument => argument.Key == "JsonSerializerContextType")
            .Select(static argument => argument.Value.Value)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        return declared ?? compilation.GetTypeByMetadataName(contextName);
    }

    private static string GetRegisteredTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static bool IsJsonAttribute(INamedTypeSymbol? attributeClass, string name)
    {
        return attributeClass is { } ac && ac.Name == name && ac.IsInNamespace("System", "Text", "Json", "Serialization");
    }

    /// <summary>
    /// The context's <c>[JsonSourceGenerationOptions]</c> that decide which members its contract writes at all.
    /// </summary>
    private sealed record ContextMemberOptions(bool IncludeFields, bool IgnoreReadOnlyProperties, bool IgnoreReadOnlyFields, int NamingPolicy)
    {
        public static ContextMemberOptions From(INamedTypeSymbol contextType)
        {
            var arguments = contextType.GetAttributes()
                .Where(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonSourceGenerationOptionsAttribute"))
                .SelectMany(static attribute => attribute.NamedArguments)
                .ToArray();

            bool Get(string name) => arguments.Any(argument => argument.Key == name && argument.Value.Value is true);

            var namingPolicy = arguments.FirstOrDefault(static argument => argument.Key == "PropertyNamingPolicy").Value.Value as int? ?? 0;

            return new ContextMemberOptions(Get(nameof(IncludeFields)), Get(nameof(IgnoreReadOnlyProperties)), Get(nameof(IgnoreReadOnlyFields)), namingPolicy);
        }
    }

    private sealed class OpenTypedMemberWalker(
        Compilation compilation,
        ParseOnlyBlueprintAppClassModel dataModelClass,
        string contextName,
        ContextMemberOptions memberOptions,
        HashSet<string> registeredTypeNames,
        HashSet<IAssemblySymbol> scannedAssemblies)
    {
        // JsonIgnoreCondition's values, which the attribute's named argument carries as an int.
        private const int IgnoreNever = 0;
        private const int IgnoreAlways = 1;
        private const int IgnoreWhenWriting = 4;

        // Blueprint's own members that are open by design: a problem's extensions and a resource event's payload and
        // bags. The runtime writes a problem or event root through reflection behind the context
        // (SerializerContextTypeInfoCache), but only a root: nested in another type, they are written by the context alone,
        // so they are exempt only on a root.
        private static readonly HashSet<string> _openByDesignRootMembers = new(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions",
            "Blueprint.Http.ResourceEvent.Data",
            "Blueprint.Http.ResourceEvent.ChangedValues",
            "Blueprint.Http.ResourceEvent.Metadata",
            "Blueprint.Http.ResourceEvent.SecureData",
        };

        // A type's members are walked once as a root and once anywhere else, since only a root exempts the above.
        private readonly HashSet<ITypeSymbol> _visitedAsRoot = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ITypeSymbol> _visitedNested = new(SymbolEqualityComparer.Default);

        // Collections being followed: a collection of itself (Tree : List<Tree>) would otherwise recurse forever. It is not
        // a visited set, so each member typed as the same collection is still checked and reported on its own.
        private readonly HashSet<ITypeSymbol> _activeCollections = new(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private readonly INamedTypeSymbol? _enumerableOfT = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        private readonly INamedTypeSymbol? _dictionaryOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        private readonly INamedTypeSymbol? _readOnlyDictionaryOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        private readonly INamedTypeSymbol? _keyValuePair = compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");

        // Set while walking the declared type of a member with [BlueprintRuntimeTypes]: an object there holds the listed
        // types, but an interface or abstract type is still written as itself, whatever the listed types say.
        private bool _objectsDeclared;

        public List<DiagnosticInfo> Diagnostics { get; } = [];

        public void Walk(ITypeSymbol rootType, string rootName)
        {
            this.VisitType(rootType, null, rootName, atRoot: true);
        }

        private void VisitType(ITypeSymbol type, ISymbol? member, string rootName, bool atRoot)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (HasConverter(type))
            {
                return;
            }

            if (this.IsOpen(type))
            {
                if (!(this._objectsDeclared && (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)))
                {
                    this.ReportOpen(member, rootName);
                }

                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                this.VisitType(array.ElementType, member, rootName, atRoot: false);
                return;
            }

            if (type.SpecialType == SpecialType.System_String || type is not INamedTypeSymbol named)
            {
                return;
            }

            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, this._keyValuePair))
            {
                // Written as an object with both a Key and a Value, unlike a dictionary entry whose key is a property name.
                this.VisitType(named.TypeArguments[0], member, rootName, atRoot: false);
                this.VisitType(named.TypeArguments[1], member, rootName, atRoot: false);
                return;
            }

            var isDictionary = this.TryGetDictionaryValueType(named, out var valueType);
            if (isDictionary || this.TryGetEnumerableElementType(named, out valueType))
            {
                if (this._activeCollections.Add(named))
                {
                    this.VisitType(valueType, member, rootName, atRoot: false);
                    this._activeCollections.Remove(named);
                }

                return;
            }

            if (named.SpecialType == SpecialType.System_Collections_IEnumerable
                || named.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
            {
                // A non-generic collection's elements are objects.
                if (!this._objectsDeclared)
                {
                    this.ReportOpen(member, rootName);
                }

                return;
            }

            var visited = atRoot ? this._visitedAsRoot : this._visitedNested;
            if (!this.IsWalkable(named) || !visited.Add(named))
            {
                return;
            }

            foreach (var serialized in this.GetSerializedMembers(named))
            {
                this.VisitMember(serialized, rootName, atRoot);
            }

            // A [JsonDerivedType] alternative is the same value written as another type, not nested in it, so it keeps
            // the root's standing: a problem root listing a subclass is still written with fallback metadata.
            foreach (var derived in GetDerivedTypes(named))
            {
                this.VisitType(derived, member, rootName, atRoot);
            }
        }

        private void VisitMember(ISymbol member, string rootName, bool atRoot)
        {
            if (HasConverter(member)
                || (atRoot && _openByDesignRootMembers.Contains(GetMemberName(member.OriginalDefinition))))
            {
                return;
            }

            var runtimeTypes = member.GetAttributes()
                .FirstOrDefault(static attribute => attribute.AttributeClass?.ToFullMetadataName() == BlueprintRuntimeTypesAttributeMetadataName);

            var objectsDeclared = this._objectsDeclared;
            this._objectsDeclared = runtimeTypes is not null;
            this.VisitType(GetMemberType(member), member, rootName, atRoot: false);
            this._objectsDeclared = false;

            if (runtimeTypes is null)
            {
                this._objectsDeclared = objectsDeclared;
                return;
            }

            foreach (var runtimeType in GetRuntimeTypes(runtimeTypes))
            {
                var runtimeTypeName = GetRegisteredTypeName(runtimeType);
                if (!registeredTypeNames.Contains(runtimeTypeName))
                {
                    this.Diagnostics.Add(new DiagnosticInfo(
                        _unregisteredRuntimeTypeRule,
                        this.GetLocation(runtimeTypes, member),
                        [GetMemberName(member), runtimeTypeName, contextName]));
                }

                // A declared runtime type is written as itself, so what it reaches is reached from the root too, and an open
                // element of a declared collection (object[]) is still the member's to report.
                this.VisitType(runtimeType, member, rootName, atRoot: false);
            }

            this._objectsDeclared = objectsDeclared;
        }

        private void ReportOpen(ISymbol? member, string rootName)
        {
            // An open root itself is not this diagnostic's: BP006 asks only that the root be declared.
            if (member is null || !this._reported.Add(GetMemberName(member)))
            {
                return;
            }

            this.Diagnostics.Add(new DiagnosticInfo(
                _openTypedResponseMemberRule,
                this.GetLocation(null, member),
                [GetMemberName(member), GetMemberType(member).ToDisplayString(), rootName, contextName]));
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
                || assembly.Name.StartsWith("Blueprint.", StringComparison.Ordinal)
                || IsProblemDetails(type);
        }

        private LocationInfo? GetLocation(AttributeData? attribute, ISymbol member)
        {
            if (attribute?.ApplicationSyntaxReference is { } attributeSyntax)
            {
                return LocationInfo.CreateFrom(attributeSyntax.GetSyntax());
            }

            var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
            return location is null ? dataModelClass.SerializerContextLocation : LocationInfo.CreateFrom(location);
        }

        /// <summary>
        /// The members the context's contract writes, as System.Text.Json decides it: public instance properties with a
        /// public getter, public fields only with <c>IncludeFields</c>, either when <c>[JsonInclude]</c>, less any
        /// <c>[JsonIgnore]</c>d always and any read-only one the context's options ignore, unless it is never ignored.
        /// </summary>
        private IEnumerable<ISymbol> GetSerializedMembers(INamedTypeSymbol type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Most-derived first, so a member that hides or overrides one on a base class is the one walked. Only a member
            // the contract considers - written, or ignored - takes its name from a base member: a non-public one hiding a
            // public base property does not, and System.Text.Json writes the base property. Names are JSON names, so a
            // derived member renamed with [JsonPropertyName] does not hide a base member it no longer collides with.
            foreach (var member in type.GetAllMembers())
            {
                if (member is not (IPropertySymbol { IsIndexer: false } or IFieldSymbol { IsConst: false, IsImplicitlyDeclared: false })
                    || member.IsStatic
                    || seen.Contains(this.GetJsonName(member)))
                {
                    continue;
                }

                var included = HasJsonAttribute(member, "JsonIncludeAttribute");
                var visible = member switch
                {
                    IPropertySymbol property => property.GetMethod is not null
                        && (included || (property.DeclaredAccessibility == Accessibility.Public && property.GetMethod.DeclaredAccessibility == Accessibility.Public)),
                    IFieldSymbol field => included || (memberOptions.IncludeFields && field.DeclaredAccessibility == Accessibility.Public),
                    _ => false,
                };

                if (!visible)
                {
                    continue;
                }

                seen.Add(this.GetJsonName(member));

                var ignoreCondition = GetIgnoreCondition(member);
                if (ignoreCondition is IgnoreAlways or IgnoreWhenWriting)
                {
                    continue;
                }

                // The read-only options leave a collection in: System.Text.Json still writes a getter-only collection.
                var ignoredAsReadOnly = ignoreCondition != IgnoreNever
                    && !this.IsCollectionOrArray(GetMemberType(member))
                    && member switch
                    {
                        IPropertySymbol property => memberOptions.IgnoreReadOnlyProperties && IsReadOnly(property, included),
                        IFieldSymbol field => memberOptions.IgnoreReadOnlyFields && field.IsReadOnly,
                        _ => false,
                    };

                if (!ignoredAsReadOnly)
                {
                    yield return member;
                }
            }
        }

        private bool IsCollectionOrArray(ITypeSymbol type)
        {
            return type is IArrayTypeSymbol || (type.SpecialType != SpecialType.System_String && this.IsCollection(type));
        }

        // ASP.NET Core's problem types are walked although they are the framework's: their open Extensions are written by
        // the context alone when a problem is nested in another response type.
        private static bool IsProblemDetails(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                // Name and namespace, not ToDisplayString, which writes a nullable-annotated ProblemDetails? with its '?'.
                if (current.Name == "ProblemDetails" && current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc")
                {
                    return true;
                }
            }

            return false;
        }

        // The member's JSON name: its [JsonPropertyName], or its name under the context's known naming policy, so two
        // members collide exactly when System.Text.Json would write one name for both.
        private string GetJsonName(ISymbol member)
        {
            return member.GetAttributes()
                .Where(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonPropertyNameAttribute"))
                .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
                .FirstOrDefault() ?? ApplyNamingPolicy(member.Name, memberOptions.NamingPolicy);
        }

        // JsonKnownNamingPolicy: 1 camelCase, 2/3 snake_case lower/upper, 4/5 kebab-case lower/upper.
        private static string ApplyNamingPolicy(string name, int policy)
        {
            return policy switch
            {
                1 => ToCamelCase(name),
                2 => ToSeparated(name, '_', upper: false),
                3 => ToSeparated(name, '_', upper: true),
                4 => ToSeparated(name, '-', upper: false),
                5 => ToSeparated(name, '-', upper: true),
                _ => name,
            };
        }

        // As JsonNamingPolicy.CamelCase: the leading run of capitals is lowered, except a capital that starts the next word.
        private static string ToCamelCase(string name)
        {
            if (name.Length == 0 || !char.IsUpper(name[0]))
            {
                return name;
            }

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (i == 1 && !char.IsUpper(chars[i]))
                {
                    break;
                }

                var hasNext = i + 1 < chars.Length;
                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    if (char.IsSeparator(chars[i + 1]))
                    {
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    }

                    break;
                }

                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }

        // An approximation of JsonNamingPolicy's snake and kebab policies: a separator before each capital that follows a
        // lower-case letter or digit, or that starts a word after a run of capitals.
        private static string ToSeparated(string name, char separator, bool upper)
        {
            var builder = new System.Text.StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c)
                    && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                {
                    builder.Append(separator);
                }

                builder.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static bool IsReadOnly(IPropertySymbol property, bool included)
        {
            return property.SetMethod is null
                || (!included && property.SetMethod.DeclaredAccessibility != Accessibility.Public);
        }

        private static int? GetIgnoreCondition(ISymbol member)
        {
            var ignore = member.GetAttributes().FirstOrDefault(static attribute => IsJsonAttribute(attribute.AttributeClass, "JsonIgnoreAttribute"));
            if (ignore is null)
            {
                return null;
            }

            // Always is the attribute's default condition.
            return ignore.NamedArguments.FirstOrDefault(static argument => argument.Key == "Condition").Value.Value as int? ?? IgnoreAlways;
        }

        private static ITypeSymbol GetMemberType(ISymbol member)
        {
            return member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;
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
            return HasJsonAttribute(type, "JsonPolymorphicAttribute") || HasJsonAttribute(type, "JsonDerivedTypeAttribute");
        }

        /// <summary>
        /// Whether a <c>[JsonConverter]</c> owns the symbol's shape, including an attribute derived from
        /// <c>JsonConverterAttribute</c> that overrides <c>CreateConverter</c>.
        /// </summary>
        private static bool HasConverter(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                for (var current = attribute.AttributeClass; current is not null; current = current.BaseType)
                {
                    if (IsJsonAttribute(current, "JsonConverterAttribute"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasJsonAttribute(ISymbol symbol, string name)
        {
            return symbol.GetAttributes().Any(attribute => IsJsonAttribute(attribute.AttributeClass, name));
        }

        private static string GetMemberName(ISymbol member)
        {
            return member.ContainingType.ToDisplayString() + "." + member.Name;
        }
    }
}
