using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules.HttpClient;

/// <summary>
/// Source-generates <c>CompiledHttpRequestFeatureData</c> on each operation descriptor so that
/// <c>BlueprintHttpClient</c> can build URLs and query strings without property reflection.
/// <para>
/// Depends on <see cref="LinkGenerationModule"/> for <see cref="UrlRegistration"/> metadata
/// and on <see cref="OperationHttpMethodMetadataModule"/> for HTTP method metadata.
/// </para>
/// </summary>
public sealed class HttpClientRequestBuilderModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string HttpClassName = "Http";

    // Private to the generated partial, so a derived operation's own does not collide with it; the prefix keeps it clear of
    // a hand-written member's name.
    private const string CustomHttpMethodFieldName = "__blueprintHttpMethod";

    private const string GeneratedCodeTool = "Blueprint.SourceGenerator";

    // No version: the build's version would change the generated text, and every snapshot of it, with each release.
    private const string GeneratedCodeMarkerAttribute = "global::System.CodeDom.Compiler.GeneratedCode(\"" + GeneratedCodeTool + "\", null)";

    // Generated files have no nullable annotation context, so a `string?` return would not compile (CS8669); the attribute
    // gives a nullable-enabled caller the same "may be null" signal.
    private const string MaybeNullReturnAttribute = "return: global::System.Diagnostics.CodeAnalysis.MaybeNull";

    // Route-owned (RoutePlaceholderSyntax, R97/R111): the bound name is whatever is before a `:` (alternate resource
    // property) or `(` (legacy format specifier) — either, or both, or neither. The old regex here only excluded `:`, so
    // a `{Name(format)}` placeholder's whole "Name(format)" was taken as the property name and never matched, leaving the
    // literal placeholder unsubstituted in the built URL.
    private static readonly Regex _placeholderRegex = RoutePlaceholderSyntax.Placeholder;

    /// <inheritdoc />
    public string ModuleId => nameof(HttpClientRequestBuilderModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HttpClientRequestBuilderModule);

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(LinkGenerationModule),
        nameof(OperationHttpMethodMetadataModule),
        nameof(PropertyOwnershipModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var urlRegistrations = context.GetMetadata<UrlRegistration>().ToList();
        if (urlRegistrations.Count == 0)
        {
            return;
        }

        var operationSymbol = context.GetOperationTypeSymbol();
        var operationFqn = operationSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Collect the first URL template — HttpClient uses the first available URL
        var urlTemplate = urlRegistrations[0].Url;

        // Extract placeholder names from the URL template
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in _placeholderRegex.Matches(urlTemplate))
        {
            placeholders.Add(match.Groups[1].Value);
        }

        // Collect all public instance properties from the operation
        // Read through the shared property model (D25), so an exposed operation's contributed properties reach its URL and
        // query string although this compilation cannot see them emitted into its partial.
        var properties = Planning.OperationPropertyModelBuilder.GetMembersWithContributed(operationSymbol)
            .OfType<IPropertySymbol>()
            .Where(static p => p.DeclaredAccessibility == Accessibility.Public
                && !p.IsStatic
                && p.GetMethod is not null
                && !p.GetAttributes().Any(static a => a.AttributeClass?.Name is "ObsoleteAttribute"))
            .ToList();

        // Read property ownership metadata to determine which properties are query params
        // and their correct binding names (camelCase, [FromQuery(Name=...)] overrides, etc.)
        var bindingEntries = context.GetMetadata<PropertyBindingEntriesMetadata>().FirstOrDefault();

        // Build a lookup: CLR property name → query binding name (only for query-source properties).
        // URL placeholder properties are excluded even if the ownership module classified them as
        // Query — the URL builder already handles them as path parameters.
        var queryBindingNames = new Dictionary<string, string>(StringComparer.Ordinal);

        // The route placeholders in only some links, which the host also reads from the query string through a link without
        // them (legacy's every-link rule): with nothing query-owned, the only properties a GET's query string carries.
        var queryFallbackNames = new HashSet<string>(StringComparer.Ordinal);
        if (bindingEntries != null)
        {
            foreach (var entry in bindingEntries.Entries)
            {
                if (entry.Source == PropertyOwnershipModule.PropertyOwnershipSource.Query
                    && !placeholders.Contains(entry.PropertyName))
                {
                    queryBindingNames[entry.PropertyName] = entry.BindingName ?? entry.PropertyName;
                }

                // An operation answering GET as well as another method reads such a property from the query string of a GET,
                // which is the request built here when one is, so it is query-owned for this request.
                if (entry.QueryOnGetBindingName is { } queryOnGetBindingName
                    && !placeholders.Contains(entry.PropertyName))
                {
                    queryBindingNames[entry.PropertyName] = queryOnGetBindingName;
                }

                if (entry.FallbackSource == PropertyOwnershipModule.PropertyOwnershipSource.Query)
                {
                    queryFallbackNames.Add(entry.PropertyName);
                }
            }
        }

        // Determine if this operation uses query string parameters: a GET carries its non-URL properties as a query string,
        // every other method (a DELETE included) as its body, as legacy's client sent them (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:79, :88-91).
        var isQueryMethod = context.GetMetadata<OperationHttpMethodMetadataRegistration>()
            .Any(static m => m.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase));

        // The builders are real static methods on a nested `Http` class of the operation's own partial, so a typed client can
        // call them without a descriptor lookup or a cast. The descriptor's delegates are thin wrappers over them, so both
        // build the same URL. An operation that cannot take the class (CanDeclareHttpClass) keeps the inline delegates only.
        var urlBuilderBody = BuildUrlBuilderBody(urlTemplate, placeholders, properties);

        // A GET carries its non-URL properties as a query string rather than a body. The typed appender exists for every
        // GET; the delegate stays null when no property is query-eligible, as the runtime reads that as "append nothing".
        var queryStringBody = isQueryMethod
            ? BuildQueryStringBody(placeholders, properties, queryBindingNames, queryFallbackNames)
            : null;

        var emitHttpClass = CanDeclareHttpClass(operationSymbol, context.GetSemanticModel);

        // The method a typed client of the operation sends it with: the one its descriptor publishes first. An attribute that
        // declares an empty one has none, which OperationHttpMethodMetadataModule reports, so no interface is implemented.
        var requestBuilderMethod = OperationHttpMethodResolver.Resolve(operationSymbol).Methods.FirstOrDefault();

        if (emitHttpClass)
        {
            context.AddOperationMemberStep(operationClass =>
            {
                // A base operation's own generated `Http` is the one inherited member this class hides on purpose. The marker is
                // how a derived operation in another assembly tells this class from a hand-written one (IsGeneratedHttpClass).
                var httpClass = operationClass.AddNestedClass(HttpClassName, Accessibility.Public)
                    .MakeStaticClass()
                    .AddAttribute(GeneratedCodeMarkerAttribute)
                    .DisableWarning("CS0108");

                httpClass.AddMethod("BuildUrl")
                    .MakePublicMethod()
                    .MakeStaticMethod()
                    .AddAttribute(MaybeNullReturnAttribute)
                    .WithReturnType("string")
                    .AddParameter(operationFqn, "operation")
                    .WithBody(codeWriter => codeWriter.AppendLine(urlBuilderBody));

                if (isQueryMethod)
                {
                    httpClass.AddMethod("AppendQueryString")
                        .MakePublicMethod()
                        .MakeStaticMethod()
                        .WithReturnType("string")
                        .AddParameter(operationFqn, "operation")
                        .AddParameter("string", "url")
                        .WithBody(codeWriter => codeWriter.AppendLine(queryStringBody ?? "return url;"));
                }

                if (requestBuilderMethod is not null)
                {
                    AddRequestBuilderInterface(operationClass, operationFqn, requestBuilderMethod);
                }
            });
        }

        context.AddRenderStep(codeWriter =>
        {
            RenderCompiledHttpRequestFeatureData(codeWriter, operationFqn, urlBuilderBody, queryStringBody, emitHttpClass);
        });
    }

    /// <summary>
    /// Whether the operation's generated partial can declare the nested <c>Http</c> builder class without changing the
    /// meaning of a name the operation can already see. Where it cannot, the descriptor's delegates carry the builders
    /// inline, as they did before the class existed, and the operation has no <c>Http</c> class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It cannot when the operation is itself named <c>Http</c> (CS0542), when any of its partial declarations declares a
    /// member of that name (CS0102), or when it inherits one it can see: the class would hide it, so an inherited
    /// <c>Http</c> property the builders read as <c>typed.Http</c> would name the class instead (CS0572).
    /// </para>
    /// <para>
    /// A base operation's own generated <c>Http</c> class does not count. The derived operation declares its own and hides
    /// it, with CS0108 suppressed. That class is a symbol only once the base is compiled into another assembly, where it is
    /// recognised by the <c>[GeneratedCode("Blueprint.SourceGenerator", …)]</c> marker this module emits on it, never by its
    /// shape; from the same compilation it is not visible at all, so an <c>Http</c> class declared in source is the user's.
    /// </para>
    /// <para>
    /// Nor can it when the operation's own code uses the simple name <c>Http</c> for anything else: an alias, a type or a
    /// namespace. Inside the operation, its nested types included, a simple name finds the operation's members first, so
    /// the class would shadow it (CS0117). A name that binds to nothing yet is the class being generated, and a local,
    /// parameter or method type parameter is nearer than any member, so neither counts.
    /// </para>
    /// <para>
    /// Only the operation's own declarations are read. The class is public and inherited, so inside a type deriving from
    /// the operation, in this assembly or another, a simple name <c>Http</c> finds it too, before a <c>using Http = …</c>
    /// alias or a namespace of that name, and such code fails with CS0117. Qualify the name there (for example
    /// <c>global::System.Net.Http.HttpMethod</c>), or alias it under another name. A derived operation declares its own class and is subject to the same limit.
    /// </para>
    /// <para>
    /// The answer turns on every partial declaration and base type of the operation, and on what its names bind to, so
    /// <c>OperationDescriptorSourceGenerator</c> carries it in the incremental model it keys each descriptor on: a colliding
    /// member or alias added to or removed from another file then re-plans the descriptor instead of replaying a cached plan.
    /// </para>
    /// </remarks>
    /// <param name="operation">The operation.</param>
    /// <param name="compilation">The compilation the operation is declared in, which binds its uses of <c>Http</c>.</param>
    /// <returns><see langword="true"/> when the nested class can be declared.</returns>
    internal static bool CanDeclareHttpClass(INamedTypeSymbol operation, Compilation compilation)
    {
        return CanDeclareHttpClass(operation, tree => compilation.GetSemanticModel(tree));
    }

    /// <summary>
    /// Whether the operation's generated partial can declare the nested <c>Http</c> builder class; see
    /// <see cref="CanDeclareHttpClass(INamedTypeSymbol, Compilation)"/>.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="semanticModelOf">
    /// The semantic model of one of the operation's syntax trees, or <see langword="null"/> when there is none, in which case
    /// any use of the simple name <c>Http</c> in that tree is taken to collide.
    /// </param>
    /// <returns><see langword="true"/> when the nested class can be declared.</returns>
    internal static bool CanDeclareHttpClass(INamedTypeSymbol operation, Func<SyntaxTree, SemanticModel?> semanticModelOf)
    {
        if (operation.Name == HttpClassName || !operation.GetMembers(HttpClassName).IsEmpty)
        {
            return false;
        }

        for (var baseType = operation.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            foreach (var member in baseType.GetMembers(HttpClassName))
            {
                if (IsVisibleToDerivedType(member, operation) && !IsGeneratedHttpClass(member))
                {
                    return false;
                }
            }
        }

        return !UsesAnotherHttp(operation, semanticModelOf);
    }

    private static bool UsesAnotherHttp(INamedTypeSymbol operation, Func<SyntaxTree, SemanticModel?> semanticModelOf)
    {
        // Only the operation's own declarations are read, never the rest of the compilation, and only a name spelt Http is
        // bound, so the cost is one token scan of each declaration.
        foreach (var reference in operation.DeclaringSyntaxReferences)
        {
            SemanticModel? semanticModel = null;
            var boundModel = false;

            foreach (var token in reference.GetSyntax().DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken)
                    || token.ValueText != HttpClassName
                    || token.Parent is not IdentifierNameSyntax name
                    || !IsLookedUpBySimpleName(name))
                {
                    continue;
                }

                if (!boundModel)
                {
                    semanticModel = semanticModelOf(reference.SyntaxTree);
                    boundModel = true;
                }

                if (semanticModel is null)
                {
                    return true;
                }

                var symbolInfo = semanticModel.GetSymbolInfo(name);
                if (WouldBeShadowed(symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsLookedUpBySimpleName(IdentifierNameSyntax name)
    {
        // Only a name looked up from the operation's scope can find the nested class: not a member named after a dot, an
        // argument or initialiser name, or an alias-qualified name, which looks up aliases alone.
        return name.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == name,
            QualifiedNameSyntax qualifiedName => qualifiedName.Left == name,
            MemberBindingExpressionSyntax or NameColonSyntax or NameEqualsSyntax or AliasQualifiedNameSyntax => false,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment => assignment.Left != name,
            _ => true,
        };
    }

    private static bool WouldBeShadowed(ISymbol? symbol)
    {
        return symbol switch
        {
            null => false,
            ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol => false,
            ITypeParameterSymbol { DeclaringMethod: not null } => false,
            _ => !IsGeneratedHttpClass(symbol),
        };
    }

    private static bool IsVisibleToDerivedType(ISymbol member, INamedTypeSymbol derivedType)
    {
        return member.DeclaredAccessibility switch
        {
            Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
            Accessibility.Internal or Accessibility.ProtectedAndInternal => member.ContainingAssembly.GivesAccessTo(derivedType.ContainingAssembly),
            _ => false,
        };
    }

    private static bool IsGeneratedHttpClass(ISymbol member)
    {
        // Positively identified, never by shape: this generator's class is never visible from the compilation it is
        // generated into, so one declared in source is the user's, and one from metadata is Blueprint's only when it carries
        // the marker the generator emits. A lookalike, with members of its own a derived Http would hide, is the user's.
        return member is INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: true, DeclaringSyntaxReferences.IsEmpty: true, ContainingType: { } operation } httpClass
            && operation.ImplementsInterface("Blueprint.Model.IOperation")
            && httpClass.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute"
                && attribute.ConstructorArguments is [{ Value: GeneratedCodeTool }, ..]);
    }

    private static void RenderCompiledHttpRequestFeatureData(
        ICodeWriter codeWriter,
        string operationFqn,
        string urlBuilderBody,
        string? queryStringBody,
        bool callHttpClass)
    {
        string buildUrl;
        string appendQueryString;

        if (callHttpClass)
        {
            buildUrl = $"static (op) => {operationFqn}.{HttpClassName}.BuildUrl(({operationFqn})op)";
            appendQueryString = queryStringBody is not null
                ? $"static (op, url) => {operationFqn}.{HttpClassName}.AppendQueryString(({operationFqn})op, url)"
                : "null";
        }
        else
        {
            buildUrl = $"static (op) => {{ var operation = ({operationFqn})op; {urlBuilderBody} }}";
            appendQueryString = queryStringBody is not null
                ? $"static (op, url) => {{ var operation = ({operationFqn})op; {queryStringBody} }}"
                : "null";
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.CompiledHttpRequestFeatureData("
            + buildUrl
            + ", "
            + appendQueryString
            + "));");
    }

    /// <summary>
    /// Implements <c>IHttpRequestBuilder&lt;TSelf&gt;</c> on the operation's generated partial over its <c>Http</c> builders, so
    /// <c>BlueprintHttpClient.ForwardAsync</c> sends the operation as its typed client does: with the method its descriptor
    /// publishes first and its first URL, a GET's query string appended. Explicitly, so it adds no member name to the
    /// operation's own.
    /// </summary>
    /// <param name="operationClass">The operation's generated partial.</param>
    /// <param name="operationFqn">The operation's fully qualified name.</param>
    /// <param name="httpMethod">The method the operation is sent with, upper-cased.</param>
    private static void AddRequestBuilderInterface(ClassBuilder operationClass, string operationFqn, string httpMethod)
    {
        var builderInterface = $"global::Blueprint.Model.IHttpRequestBuilder<{operationFqn}>";

        operationClass.AddInterface(builderInterface);

        // A standard method is HttpMethod's own shared instance, as a typed client sends it. Any other is made once, into a
        // private field, so a forward neither parses nor allocates a method per call.
        var methodExpression = OperationHttpMethodResolver.StandardHttpMethodExpression(httpMethod);

        if (methodExpression is null)
        {
            methodExpression = CustomHttpMethodFieldName;

            operationClass.AddProperty(CustomHttpMethodFieldName, Accessibility.Private)
                .SetType("global::System.Net.Http.HttpMethod")
                .MakeStatic()
                .WithReadonlyValue($"new global::System.Net.Http.HttpMethod({SymbolDisplay.FormatLiteral(httpMethod, quote: true)})");
        }

        operationClass.AddMethod(builderInterface + ".GetHttpMethod")
            .MakeStaticMethod()
            .WithReturnType("global::System.Net.Http.HttpMethod")
            .WithBody(codeWriter => codeWriter.AppendLine($"return {methodExpression};"));

        operationClass.AddMethod(builderInterface + ".BuildRequestUrl")
            .MakeStaticMethod()
            .AddAttribute(MaybeNullReturnAttribute)
            .WithReturnType("string")
            .AddParameter(operationFqn, "operation")
            .WithBody(codeWriter =>
            {
                if (httpMethod == "GET")
                {
                    codeWriter.AppendLine($"var url = {HttpClassName}.BuildUrl(operation);");
                    codeWriter.AppendLine($"return url is null ? null : {HttpClassName}.AppendQueryString(operation, url);");
                }
                else
                {
                    codeWriter.AppendLine($"return {HttpClassName}.BuildUrl(operation);");
                }
            });
    }

    private static string BuildUrlBuilderBody(
        string urlTemplate,
        HashSet<string> placeholders,
        List<IPropertySymbol> properties)
    {
        // Build a property lookup: property name (case-insensitive) → property symbol
        var propertyLookup = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties)
        {
            propertyLookup[prop.Name] = prop;
        }

        bool CanBeNull(string name) => propertyLookup.TryGetValue(name, out var p) && RoutePlaceholderSyntax.CanBeNull(p.Type);

        // A null property's placeholder sends its default ({Kind=all}), or is left out when it is one of the optional
        // placeholders ({Ext?}) that end the template, rather than leaving the operation without a URL.
        var omissible = RoutePlaceholderSyntax.GetOmissible(urlTemplate, CanBeNull);
        var trailingMatches = RoutePlaceholderSyntax.GetTrailingOptional(urlTemplate, CanBeNull);
        var trailingOptional = trailingMatches.Select(static match => match.Groups[1].Value).ToList();

        // Generate null checks for placeholder properties
        var nullChecks = new List<string>();

        foreach (var placeholder in placeholders)
        {
            if (!propertyLookup.TryGetValue(placeholder, out var prop) || omissible.Contains(placeholder))
            {
                continue;
            }

            if (RoutePlaceholderSyntax.CanBeNull(prop.Type))
            {
                nullChecks.Add($"typed.{prop.Name} is null");
            }
        }

        // An optional segment left out before one that is set would move that value into its place, so there is no URL. One
        // after a '.' leaves its segment there, so nothing moves.
        for (var i = 0; i + 1 < trailingOptional.Count; i++)
        {
            if (!RoutePlaceholderSyntax.IsWholeSegment(urlTemplate, trailingMatches[i]))
            {
                continue;
            }

            nullChecks.Add($"(typed.{propertyLookup[trailingOptional[i]].Name} is null && typed.{propertyLookup[trailingOptional[i + 1]].Name} is not null)");
        }

        // Build URL using string concatenation with Uri.EscapeDataString
        // We avoid $"" interpolation because global:: is not valid inside interpolation holes in C# < 13
        var segments = new List<string>();
        var lastEnd = 0;

        foreach (Match match in _placeholderRegex.Matches(urlTemplate))
        {
            var name = match.Groups[1].Value;

            // RoutePlaceholderSyntax.Placeholder has no separate "star" group (the `\*{0,2}` is inline, uncaptured), so a
            // catch-all is read off the placeholder's own text instead.
            var isCatchAll = match.Value.Length > 1 && match.Value[1] == '*';

            if (!propertyLookup.TryGetValue(name, out var prop))
            {
                continue;
            }

            var literal = urlTemplate.Substring(lastEnd, match.Index - lastEnd);
            var matchEnd = match.Index + match.Length;
            var isLeftOut = trailingOptional.Contains(name, StringComparer.OrdinalIgnoreCase);
            var defaultValue = RoutePlaceholderSyntax.CanBeNull(prop.Type) ? RoutePlaceholderSyntax.GetDefault(match) : null;

            // An optional placeholder left out takes the '.' or '/' before it along, as the router reads
            // "/files/{Name}.{Ext?}" from "/files/abc".
            var optionalSeparator = string.Empty;
            if (isLeftOut && literal.Length > 0 && literal[^1] is '.' or '/')
            {
                optionalSeparator = literal[^1].ToString();
                literal = literal[..^1];
            }

            // Add literal segment before this placeholder
            if (literal.Length > 0)
            {
                segments.Add($"\"{literal}\"");
            }

            // Catch-all parameters (e.g. {*FolderPath} or {**FolderPath}) preserve slashes
            // and must not be URL-encoded. Regular parameters are escaped.
            var propertyAccess = $"typed.{prop.Name}";
            string value;
            if (isCatchAll)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    value = propertyAccess;
                }
                else if (prop.Type.IsValueType && prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    value = $"{propertyAccess}.Value.ToString()!";
                }
                else
                {
                    value = $"{propertyAccess}.ToString()!";
                }
            }
            else if (prop.Type.SpecialType == SpecialType.System_String)
            {
                value = $"global::System.Uri.EscapeDataString({propertyAccess})";
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                value = $"global::System.Uri.EscapeDataString({propertyAccess}.Value.ToString()!)";
            }
            else
            {
                value = $"global::System.Uri.EscapeDataString({propertyAccess}.ToString()!)";
            }

            if (isLeftOut)
            {
                var present = optionalSeparator.Length > 0 ? $"\"{optionalSeparator}\" + {value}" : value;
                value = $"({propertyAccess} is null ? \"\" : {present})";
            }
            else if (defaultValue is not null)
            {
                // The default goes in the URL in its place, wherever it is, so a later segment cannot move into its place.
                var sent = isCatchAll ? defaultValue : Uri.EscapeDataString(defaultValue);
                value = $"({propertyAccess} is null ? {SymbolDisplay.FormatLiteral(sent, quote: true)} : {value})";
            }

            segments.Add(value);

            lastEnd = matchEnd;
        }

        // Add remaining literal after last placeholder
        if (lastEnd < urlTemplate.Length)
        {
            segments.Add($"\"{urlTemplate[lastEnd..]}\"");
        }

        // Ensure leading slash
        var urlExpr = segments.Count > 0
            ? string.Join(" + ", segments)
            : $"\"{urlTemplate}\"";

        if (!urlTemplate.StartsWith('/'))
        {
            urlExpr = "\"/\" + " + urlExpr;
        }

        var nullCheckExpr = nullChecks.Count > 0
            ? $"if ({string.Join(" || ", nullChecks)}) return null; "
            : string.Empty;

        return $"var typed = operation; {nullCheckExpr}return {urlExpr};";
    }

    private static string? BuildQueryStringBody(
        HashSet<string> placeholders,
        List<IPropertySymbol> properties,
        Dictionary<string, string> queryBindingNames,
        HashSet<string> queryFallbackNames)
    {
        // The query string carries only what the host reads from one: the query-owned properties, or with none of those a
        // route placeholder in only some links that the built URL leaves out. A property ownership gave no entry (getter-only,
        // a system property such as a CancellationToken) is bound from nothing, so it is never sent; nor is anything of an
        // operation with no ownership metadata, which for one with a link means one with no bindable property, or one whose
        // ownership analysis reported an error (BPPO003, BPPO006, BPPO009). Those fail the build unless a consumer downgrades
        // their severity, in which case the host still binds the valid query properties but the client sends none of them.
        // The non-placeholder-property heuristic this replaced used to send them all.
        List<IPropertySymbol> queryProperties;
        if (queryBindingNames.Count > 0)
        {
            queryProperties = properties
                .Where(p => queryBindingNames.ContainsKey(p.Name))
                .ToList();
        }
        else
        {
            queryProperties = properties
                .Where(p => queryFallbackNames.Contains(p.Name) && !placeholders.Contains(p.Name))
                .ToList();
        }

        if (queryProperties.Count == 0)
        {
            return null;
        }

        // Determine whether any property is a collection — collections need List<string>
        // for repeated key expansion (e.g. ?ids=1&ids=2). Scalar-only operations use a
        // zero-allocation StringBuilder approach with separator tracking.
        var hasCollections = queryProperties.Any(p => IsEnumerableType(p.Type, out _));

        return hasCollections
            ? BuildQueryStringBodyWithList(queryProperties, queryBindingNames)
            : BuildQueryStringBodyScalarOnly(queryProperties, queryBindingNames);
    }

    /// <summary>
    /// Generates a query string builder body for operations where ALL query parameters are scalar
    /// (no collections). Uses a zero-allocation separator-tracking pattern instead of List.
    /// </summary>
    private static string BuildQueryStringBodyScalarOnly(
        List<IPropertySymbol> queryProperties,
        Dictionary<string, string> queryBindingNames)
    {
        // Pattern: track separator char, build string with direct concatenation.
        // Generated code shape:
        //   var sep = url.Contains('?') ? '&' : '?';
        //   var qs = url;
        //   qs += sep + "prop=..." ; sep = '&';
        //   return qs;
        var parts = new List<string>
        {
            "var typed = operation;",
            "var sep = url.Contains('?') ? '&' : '?';",
            "var qs = url;",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"typed.{prop.Name}";
            var bindingName = ResolveQueryBindingName(prop.Name, queryBindingNames);
            var escapedName = Uri.EscapeDataString(bindingName);

            if (prop.Type.SpecialType == SpecialType.System_Boolean)
            {
                parts.Add($"qs = qs + sep + \"{escapedName}=\" + ({propAccess} ? \"true\" : \"false\"); sep = '&';");
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                if (HasNonEmptyToString(prop.Type))
                {
                    parts.Add($"qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.ToString()!); sep = '&';");
                }
                else
                {
                    // A struct's ToString() may be null or empty when default (e.g. Signature365's ApiId): skip the key.
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString(s); sep = '&'; }} }}");
                }
            }
            else
            {
                parts.Add($"if ({propAccess} is not null)");
                parts.Add("{");

                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    parts.Add($"if ({propAccess}.Length > 0) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}); sep = '&'; }}");
                }
                else if (prop.Type.IsValueType)
                {
                    parts.Add($"{{ var s = {propAccess}.Value.ToString(); if (!string.IsNullOrEmpty(s)) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString(s); sep = '&'; }} }}");
                }
                else
                {
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString(s); sep = '&'; }} }}");
                }

                parts.Add("}");
            }
        }

        parts.Add("return qs == url ? url : qs;");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Generates a query string builder body for operations that have collection parameters.
    /// Uses List&lt;string&gt; for repeated-key expansion (e.g. ?ids=1&amp;ids=2).
    /// </summary>
    private static string BuildQueryStringBodyWithList(
        List<IPropertySymbol> queryProperties,
        Dictionary<string, string> queryBindingNames)
    {
        var parts = new List<string>
        {
            "var typed = operation;",
            "var parts = new global::System.Collections.Generic.List<string>();",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"typed.{prop.Name}";
            var bindingName = ResolveQueryBindingName(prop.Name, queryBindingNames);
            var escapedName = Uri.EscapeDataString(bindingName);

            if (IsEnumerableType(prop.Type, out var elementType))
            {
                EmitCollectionQueryParam(parts, propAccess, escapedName, elementType);
            }
            else if (prop.Type.SpecialType == SpecialType.System_Boolean)
            {
                parts.Add($"parts.Add(\"{escapedName}=\" + ({propAccess} ? \"true\" : \"false\"));");
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                if (HasNonEmptyToString(prop.Type))
                {
                    parts.Add($"parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.ToString()!));");
                }
                else
                {
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
                }
            }
            else
            {
                parts.Add($"if ({propAccess} is not null)");
                parts.Add("{");

                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    parts.Add($"if ({propAccess}.Length > 0) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}));");
                }
                else if (prop.Type.IsValueType)
                {
                    parts.Add($"{{ var s = {propAccess}.Value.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
                }
                else
                {
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
                }

                parts.Add("}");
            }
        }

        parts.Add("if (parts.Count == 0) return url;");
        parts.Add("var sep = url.Contains('?') ? \"&\" : \"?\";");
        parts.Add("return url + sep + string.Join(\"&\", parts);");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> is a collection that should be expanded as
    /// repeated query-string keys (e.g. <c>?ids=1&amp;ids=2</c>). Returns the element type
    /// when the type is an array or implements <c>IEnumerable&lt;T&gt;</c> (excluding
    /// <see cref="string"/> which is itself <c>IEnumerable&lt;char&gt;</c>).
    /// </summary>
    private static bool IsEnumerableType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        // string is IEnumerable<char> but should not be treated as a collection
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        // Arrays: T[]
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        // IEnumerable<T>, IReadOnlyList<T>, List<T>, IList<T>, etc.
        foreach (var iface in type.AllInterfaces)
        {
            if (iface is INamedTypeSymbol { IsGenericType: true } named
                && named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                elementType = named.TypeArguments[0];
                return true;
            }
        }

        // Also check if the type itself is IEnumerable<T>
        if (type is INamedTypeSymbol { IsGenericType: true } self
            && self.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            elementType = self.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits a <c>foreach</c> loop that appends one query-string key per element,
    /// producing the standard repeated-key encoding (e.g. <c>?ids=1&amp;ids=2&amp;ids=3</c>).
    /// </summary>
    private static void EmitCollectionQueryParam(
        List<string> parts,
        string propAccess,
        string escapedName,
        ITypeSymbol? elementType)
    {
        parts.Add($"if ({propAccess} is not null)");
        parts.Add("{");
        parts.Add($"foreach (var _item in {propAccess})");
        parts.Add("{");

        if (elementType?.SpecialType == SpecialType.System_String)
        {
            parts.Add($"if (_item is not null && _item.Length > 0) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(_item));");
        }
        else if (elementType?.SpecialType == SpecialType.System_Boolean)
        {
            parts.Add($"parts.Add(\"{escapedName}=\" + (_item ? \"true\" : \"false\"));");
        }
        else if (elementType is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated } && HasNonEmptyToString(elementType))
        {
            // Built-in value types (int, enums, etc.) — ToString() is never null or empty
            parts.Add($"parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(_item.ToString()!));");
        }
        else
        {
            // Reference types, nullable value types and structs whose ToString() may be null or empty — skip those.
            // A non-nullable struct has no null to propagate, so `?.` is only valid on the others.
            var access = elementType is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated } ? "_item" : "_item?";
            parts.Add($"{{ var s = {access}.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
        }

        parts.Add("}");
        parts.Add("}");
    }

    /// <summary>
    /// Whether <c>ToString()</c> on <paramref name="type"/> can never be null or empty: the built-in
    /// primitives and enums. Any other struct (e.g. an id whose <c>ToString()</c> returns null when default)
    /// has its value checked before the key is emitted.
    /// </summary>
    private static bool HasNonEmptyToString(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Enum || type.SpecialType != SpecialType.None;
    }

    /// <summary>
    /// Resolves the query string key for a property by looking up the binding name assigned by
    /// <see cref="PropertyOwnershipModule"/>. Falls back to <see cref="GetCamelCaseName"/> when
    /// ownership metadata is not available (defensive — should not happen in normal flow).
    /// </summary>
    private static string ResolveQueryBindingName(string propertyName, Dictionary<string, string> queryBindingNames)
    {
        if (queryBindingNames.TryGetValue(propertyName, out var bindingName))
        {
            return bindingName;
        }

        return GetCamelCaseName(propertyName);
    }

    /// <summary>
    /// Lowercases the first character of a property name, matching the convention used by
    /// <see cref="PropertyOwnershipModule"/> for query string binding names.
    /// </summary>
    private static string GetCamelCaseName(string name)
    {
        return name.Camelize();
    }
}
