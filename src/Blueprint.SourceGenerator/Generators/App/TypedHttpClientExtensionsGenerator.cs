using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.App;

/// <summary>
/// Incremental source generator that emits strongly-typed extension methods on
/// <c>BlueprintHttpClient&lt;TApp&gt;</c> for every operation registered through
/// a <c>[BlueprintAppScan]</c> app class. Each generated method constructs the
/// operation, populates its properties from method parameters, and calls the
/// underlying typed <c>SendAsync</c> — giving consumers IntelliSense-driven,
/// compile-time-safe service-to-service calls with zero runtime overhead beyond
/// the existing <c>BlueprintHttpClient</c> infrastructure.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TypedHttpClientExtensionsGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ExtractAppAndOperations(ctx, ct))
            .Where(static model => model is not null)
            .WithTrackingName(Stages.InitialExtraction);

        context.RegisterSourceOutput(
            classProvider,
            static (spc, model) =>
            {
                if (model is null || model.Operations.Count == 0)
                {
                    return;
                }

                spc.AddSource(
                    $"{model.AppClassName}_HttpClientExtensions.g.cs",
                    SourceText.From(GenerateExtensions(model), Encoding.UTF8));
            });
    }

    private static TypedClientModel? ExtractAppAndOperations(
        GeneratorAttributeSyntaxContext context,
        System.Threading.CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol appType)
        {
            return null;
        }

        // Must be partial
        if (context.TargetNode is ClassDeclarationSyntax classDecl
            && !classDecl.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
        {
            return null;
        }

        var appNamespace = appType.ContainingNamespace.IsGlobalNamespace
            ? null
            : appType.ContainingNamespace.ToDisplayString();
        var appClassName = appType.Name;
        var appFqn = appType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Check if Blueprint.HttpClient assembly is referenced
        var httpClientType = context.SemanticModel.Compilation.GetTypeByMetadataName(
            "Blueprint.HttpClient.BlueprintHttpClient`1");
        if (httpClientType is null)
        {
            // BlueprintHttpClient not referenced — skip generation
            return null;
        }

        var operationInterface = context.SemanticModel.Compilation.GetTypeByMetadataName("Blueprint.Model.IOperation");
        if (operationInterface is null)
        {
            return null;
        }

        // Scan for operations using the same logic as AppDataModelSourceGenerator
        var operations = new List<TypedClientOperationModel>();

        foreach (var attr in appType.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass is not { Name: "BlueprintAppScanAttribute" } attrClass
                || !attrClass.IsInNamespace("Blueprint", "Model"))
            {
                continue;
            }

            IAssemblySymbol? targetAssembly;
            if (attr.NamedArguments.Any(static kv => kv.Key == "FromAssemblyOf"))
            {
                var fromAssemblyOf = attr.NamedArguments
                    .First(static kv => kv.Key == "FromAssemblyOf").Value;
                if (fromAssemblyOf.Value is INamedTypeSymbol externalType)
                {
                    targetAssembly = externalType.ContainingAssembly;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                targetAssembly = appType.ContainingAssembly;
            }

            if (targetAssembly is null)
            {
                continue;
            }

            foreach (var type in GetAllTypesFromAssembly(targetAssembly))
            {
                ct.ThrowIfCancellationRequested();

                if (type.IsAbstract || type.IsStatic || !type.CanBeReferencedByName
                    || type.TypeKind != TypeKind.Class || type.IsGenericType)
                {
                    continue;
                }

                if (!type.IsAssignableTo(operationInterface))
                {
                    continue;
                }

                var operationModel = BuildOperationModel(type);
                if (operationModel is not null)
                {
                    operations.Add(operationModel);
                }
            }
        }

        if (operations.Count == 0)
        {
            return null;
        }

        return new TypedClientModel(appNamespace, appClassName, appFqn, new EquatableList<TypedClientOperationModel>(operations.ToArray()));
    }

    private static TypedClientOperationModel? BuildOperationModel(INamedTypeSymbol operationType)
    {
        var operationFqn = operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Skip operations explicitly marked as unexposed (not HTTP-accessible)
        if (operationType.GetAttributes().Any(static a => a.AttributeClass?.Name == "UnexposedOperationAttribute"))
        {
            return null;
        }

        // Determine the result type from IQuery<T> or ICommand<T>
        var (resultTypeFqn, isResultValueType) = GetResultTypeInfo(operationType);

        // Detect ResourceEvent<T> result types so the generated method uses
        // SendResourceEventAsync<T> (envelope-aware) instead of SendAsync<ResourceEvent<T>>
        var resourceEventResourceTypeFqn = GetResourceEventInnerType(operationType);

        // Get the HTTP method from explicit attributes or interface convention
        var httpMethod = GetHttpMethod(operationType);
        if (httpMethod is null)
        {
            return null;
        }

        // Get public settable properties as parameters
        var parameters = new List<TypedClientParameterModel>();
        foreach (var prop in operationType.GetAllMembers().OfType<IPropertySymbol>())
        {
            if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic)
            {
                continue;
            }

            if (prop.SetMethod is null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
            {
                // Check for init-only
                if (prop.SetMethod is null || !prop.SetMethod.IsInitOnly)
                {
                    continue;
                }
            }

            if (prop.GetAttributes().Any(static a => a.AttributeClass?.Name is "ObsoleteAttribute" or "JsonIgnoreAttribute"))
            {
                continue;
            }

            var typeFqn = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isRequired = prop.IsRequired
                || prop.GetAttributes().Any(static a => a.AttributeClass?.Name is "RequiredAttribute");

            // FullyQualifiedFormat omits the `?` suffix for nullable reference types — add it
            if (isNullable && !typeFqn.EndsWith("?", StringComparison.Ordinal))
            {
                typeFqn += "?";
            }

            parameters.Add(new TypedClientParameterModel(
                prop.Name,
                typeFqn,
                isNullable,
                isRequired,
                prop.Type.IsValueType));
        }

        // Generate a clean method name from the operation type name
        var methodName = GenerateMethodName(operationType.Name, httpMethod);

        return new TypedClientOperationModel(
            operationFqn,
            operationType.Name,
            methodName,
            resultTypeFqn,
            isResultValueType,
            httpMethod,
            new EquatableList<TypedClientParameterModel>(parameters.ToArray()),
            resourceEventResourceTypeFqn);
    }

    private static (string? Fqn, bool IsValueType) GetResultTypeInfo(INamedTypeSymbol operationType)
    {
        foreach (var iface in operationType.AllInterfaces)
        {
            if (iface.TypeArguments.Length == 1
                && iface.IsInNamespace("Blueprint", "Model")
                && iface.Name is "ICommand" or "IQuery" or "IOperation")
            {
                var resultType = iface.TypeArguments[0];

                // Skip OperationResult base — means no typed result
                if (resultType is INamedTypeSymbol namedResult
                    && namedResult.Name == "OperationResult"
                    && namedResult.IsInNamespace("Blueprint", "Runtime", "Exec"))
                {
                    return (null, false);
                }

                return (resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), resultType.IsValueType);
            }
        }

        return (null, false);
    }

    /// <summary>
    /// If the result type is <c>ResourceEvent&lt;T&gt;</c>, <c>ResourceCreated&lt;T&gt;</c>,
    /// <c>ResourceUpdated&lt;T&gt;</c>, or <c>ResourceDeleted&lt;T&gt;</c>, returns the FQN
    /// of the inner resource type <c>T</c>. Otherwise returns <c>null</c>.
    /// </summary>
    private static string? GetResourceEventInnerType(INamedTypeSymbol operationType)
    {
        foreach (var iface in operationType.AllInterfaces)
        {
            if (iface.TypeArguments.Length != 1
                || !iface.IsInNamespace("Blueprint", "Model"))
            {
                continue;
            }

            if (iface.Name is not ("ICommand" or "IQuery" or "IOperation"))
            {
                continue;
            }

            var resultType = iface.TypeArguments[0];
            return ExtractResourceEventResourceType(resultType);
        }

        return null;
    }

    /// <summary>
    /// Walks the type hierarchy to find <c>ResourceEvent&lt;T&gt;</c> and extract <c>T</c>.
    /// Handles <c>ResourceEvent&lt;T&gt;</c>, <c>ResourceCreated&lt;T&gt;</c>,
    /// <c>ResourceUpdated&lt;T&gt;</c>, and <c>ResourceDeleted&lt;T&gt;</c>.
    /// </summary>
    private static string? ExtractResourceEventResourceType(ITypeSymbol resultType)
    {
        if (resultType is not INamedTypeSymbol named)
        {
            return null;
        }

        // Check direct match: ResourceEvent<T>
        if (named.IsGenericType
            && named.TypeArguments.Length == 1
            && named.Name == "ResourceEvent"
            && named.IsInNamespace("Blueprint", "Http"))
        {
            return named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Check subclasses: ResourceCreated<T>, ResourceUpdated<T>, ResourceDeleted<T>
        // These extend ResourceEvent<T> which extends ResourceEvent
        var current = named.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType
                && current.TypeArguments.Length == 1
                && current.Name == "ResourceEvent"
                && current.IsInNamespace("Blueprint", "Http"))
            {
                return current.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            current = current.BaseType;
        }

        return null;
    }

    private static string? GetHttpMethod(INamedTypeSymbol operationType)
    {
        // Check for explicit HTTP method attributes (same as OperationHttpMethodMetadataModule)
        foreach (var attr in operationType.GetAttributes())
        {
            if (attr.AttributeClass is not { } attrClass
                || !attrClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            switch (attrClass.Name)
            {
                case "HttpGetAttribute":
                    return "GET";
                case "HttpPostAttribute":
                    return "POST";
                case "HttpPutAttribute":
                    return "PUT";
                case "HttpPatchAttribute":
                    return "PATCH";
                case "HttpDeleteAttribute":
                    return "DELETE";
                case "HttpMethodAttribute"
                    when attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string method:
                    return method.ToUpperInvariant();
            }
        }

        // Fall back to interface convention
        foreach (var iface in operationType.AllInterfaces)
        {
            if (iface.IsInNamespace("Blueprint", "Model"))
            {
                if (iface.Name == "IQuery")
                {
                    return "GET";
                }

                if (iface.Name == "ICommand")
                {
                    return "POST";
                }
            }
        }

        return null;
    }

    private static string GenerateMethodName(string operationTypeName, string httpMethod)
    {
        // Remove common suffixes
        var name = operationTypeName;
        if (name.EndsWith("Query", StringComparison.Ordinal))
        {
            name = name[..^5];
        }
        else if (name.EndsWith("Command", StringComparison.Ordinal))
        {
            name = name[..^7];
        }
        else if (name.EndsWith("Task", StringComparison.Ordinal))
        {
            name = name[..^4];
        }

        return name + "Async";
    }

    private static string GenerateExtensions(TypedClientModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("//------------------------------------------------------------------------------");
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     This code was generated by the Blueprint source generator.");
        sb.AppendLine("//");
        sb.AppendLine("//     Changes to this file may cause incorrect behavior and will be lost if");
        sb.AppendLine("//     the code is regenerated.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("//------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var extensionNamespace = model.AppNamespace is not null
            ? model.AppNamespace
            : "Blueprint.Generated";

        sb.AppendLine($"namespace {extensionNamespace}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Provides strongly-typed extension methods for calling operations registered with");
        sb.AppendLine($"    /// <see cref=\"{model.AppFqn.Replace("global::", string.Empty, StringComparison.Ordinal)}\"/>");
        sb.AppendLine($"    /// via <see cref=\"global::Blueprint.HttpClient.BlueprintHttpClient{{TApp}}\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Blueprint.SourceGenerator\", \"1.0.0\")]");
        sb.AppendLine($"    public static class {model.AppClassName}HttpClientExtensions");
        sb.AppendLine("    {");

        var first = true;
        foreach (var op in model.Operations)
        {
            if (!first)
            {
                sb.AppendLine();
            }

            first = false;
            GenerateMethod(sb, model, op);

            // Generate an operation-object overload that accepts the pre-built operation
            // directly. This reduces S365 migration churn — gateway handlers just change
            // the client type without rewriting the call site.
            sb.AppendLine();
            GenerateOperationObjectOverload(sb, model, op);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateMethod(StringBuilder sb, TypedClientModel model, TypedClientOperationModel op)
    {
        // Determine which params are positional (no default) vs optional (= default)
        // A param is positional if: [Required] OR (non-nullable - neither reference ? nor value ?)
        bool IsPositional(TypedClientParameterModel p) =>
            p.IsRequired || !p.IsNullable;

        var positionalParams = op.Parameters.Where(IsPositional)
            .OrderBy(static p => p.Name, StringComparer.Ordinal).ToList();
        var optionalParams = op.Parameters.Where(p => !IsPositional(p))
            .OrderBy(static p => p.Name, StringComparer.Ordinal).ToList();

        // Build parameter list
        var paramParts = new List<string>
        {
            $"this global::Blueprint.HttpClient.BlueprintHttpClient<{model.AppFqn}> client",
        };

        foreach (var param in positionalParams)
        {
            var paramName = ToCamelCase(param.Name);
            paramParts.Add($"{param.TypeFqn} {paramName}");
        }

        foreach (var param in optionalParams)
        {
            var paramName = ToCamelCase(param.Name);
            paramParts.Add($"{param.TypeFqn} {paramName} = default");
        }

        paramParts.Add("global::System.Threading.CancellationToken cancellationToken = default");

        // Determine return type - SendAsync<T> returns Task<T?> but for value types T? is just T (no Nullable wrapper without struct constraint)
        var hasTypedResult = op.ResultTypeFqn is not null;
        var isResourceEvent = op.ResourceEventResourceTypeFqn is not null;
        string returnType;
        if (isResourceEvent)
        {
            // Use ResourceEvent<TResource> as the return type, even when the declared result
            // is a subtype like ResourceCreated<T>. The HTTP envelope always deserializes as
            // ResourceEvent<T> via SendResourceEventEnvelopeAsync.
            returnType = $"global::System.Threading.Tasks.Task<global::Blueprint.Http.ResourceEvent<{op.ResourceEventResourceTypeFqn}>?>";
        }
        else if (hasTypedResult)
        {
            returnType = op.IsResultValueType
                ? $"global::System.Threading.Tasks.Task<{op.ResultTypeFqn}>"
                : $"global::System.Threading.Tasks.Task<{op.ResultTypeFqn}?>";
        }
        else
        {
            returnType = "global::System.Threading.Tasks.Task";
        }

        // XML doc
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Sends a <see cref=\"{op.OperationFqn.Replace("global::", string.Empty, StringComparison.Ordinal)}\"/> ({op.HttpMethod}) to the remote API.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <param name=\"client\">The Blueprint HTTP client.</param>");
        foreach (var param in positionalParams.Concat(optionalParams))
        {
            sb.AppendLine($"        /// <param name=\"{ToCamelCase(param.Name)}\">Maps to {op.OperationTypeName}.{param.Name}.</param>");
        }

        sb.AppendLine($"        /// <param name=\"cancellationToken\">Cancellation token.</param>");
        if (hasTypedResult)
        {
            sb.AppendLine($"        /// <returns>The deserialized response.</returns>");
        }

        // Method signature
        sb.Append($"        public static {returnType} {op.MethodName}(");
        sb.Append(string.Join(", ", paramParts));
        sb.AppendLine(")");
        sb.AppendLine("        {");

        // Body: construct operation and call SendAsync
        sb.AppendLine($"            var operation = new {op.OperationFqn}");
        sb.AppendLine("            {");

        foreach (var param in positionalParams)
        {
            var paramName = ToCamelCase(param.Name);
            sb.AppendLine($"                {param.Name} = {paramName},");
        }

        foreach (var param in optionalParams)
        {
            var paramName = ToCamelCase(param.Name);
            sb.AppendLine($"                {param.Name} = {paramName},");
        }

        sb.AppendLine("            };");
        sb.AppendLine();

        if (isResourceEvent)
        {
            sb.AppendLine($"            return client.SendResourceEventEnvelopeAsync<{op.ResourceEventResourceTypeFqn}>(operation, cancellationToken);");
        }
        else if (hasTypedResult)
        {
            sb.AppendLine($"            return client.SendAsync<{op.ResultTypeFqn}>(operation, cancellationToken);");
        }
        else
        {
            sb.AppendLine("            return client.SendAsync(operation, cancellationToken);");
        }

        sb.AppendLine("        }");
    }

    /// <summary>
    /// Generates an overload that accepts the pre-built operation object directly,
    /// rather than flattened parameters. This is the pattern S365 gateway handlers use
    /// and reduces migration churn to just a client-type change.
    /// </summary>
    private static void GenerateOperationObjectOverload(StringBuilder sb, TypedClientModel model, TypedClientOperationModel op)
    {
        var hasTypedResult = op.ResultTypeFqn is not null;
        var isResourceEvent = op.ResourceEventResourceTypeFqn is not null;
        string returnType;
        if (isResourceEvent)
        {
            returnType = $"global::System.Threading.Tasks.Task<global::Blueprint.Http.ResourceEvent<{op.ResourceEventResourceTypeFqn}>?>";
        }
        else if (hasTypedResult)
        {
            returnType = op.IsResultValueType
                ? $"global::System.Threading.Tasks.Task<{op.ResultTypeFqn}>"
                : $"global::System.Threading.Tasks.Task<{op.ResultTypeFqn}?>";
        }
        else
        {
            returnType = "global::System.Threading.Tasks.Task";
        }

        // Overload 1: operation + cancellation token (no CallOptions)
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Sends a pre-built <see cref=\"{op.OperationFqn.Replace("global::", string.Empty, StringComparison.Ordinal)}\"/> ({op.HttpMethod}) to the remote API.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <param name=\"client\">The Blueprint HTTP client.</param>");
        sb.AppendLine($"        /// <param name=\"operation\">The operation to send.</param>");
        sb.AppendLine($"        /// <param name=\"cancellationToken\">Cancellation token.</param>");
        if (hasTypedResult || isResourceEvent)
        {
            sb.AppendLine($"        /// <returns>The deserialized response.</returns>");
        }

        sb.Append($"        public static {returnType} {op.MethodName}(");
        sb.Append($"this global::Blueprint.HttpClient.BlueprintHttpClient<{model.AppFqn}> client, ");
        sb.Append($"{op.OperationFqn} operation, ");
        sb.Append("global::System.Threading.CancellationToken cancellationToken = default");
        sb.AppendLine(")");
        sb.AppendLine("        {");

        if (isResourceEvent)
        {
            sb.AppendLine($"            return client.SendResourceEventEnvelopeAsync<{op.ResourceEventResourceTypeFqn}>(operation, cancellationToken);");
        }
        else if (hasTypedResult)
        {
            sb.AppendLine($"            return client.SendAsync<{op.ResultTypeFqn}>(operation, cancellationToken);");
        }
        else
        {
            sb.AppendLine("            return client.SendAsync(operation, cancellationToken);");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Overload 2: operation + CallOptions + cancellation token (S365 exact signature)
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Sends a pre-built <see cref=\"{op.OperationFqn.Replace("global::", string.Empty, StringComparison.Ordinal)}\"/> ({op.HttpMethod}) to the remote API");
        sb.AppendLine("        /// with optional per-request customisation.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <param name=\"client\">The Blueprint HTTP client.</param>");
        sb.AppendLine($"        /// <param name=\"operation\">The operation to send.</param>");
        sb.AppendLine($"        /// <param name=\"options\">Optional call options to modify the outgoing request.</param>");
        sb.AppendLine($"        /// <param name=\"cancellationToken\">Cancellation token.</param>");
        if (hasTypedResult || isResourceEvent)
        {
            sb.AppendLine($"        /// <returns>The deserialized response.</returns>");
        }

        sb.Append($"        public static {returnType} {op.MethodName}(");
        sb.Append($"this global::Blueprint.HttpClient.BlueprintHttpClient<{model.AppFqn}> client, ");
        sb.Append($"{op.OperationFqn} operation, ");
        sb.Append("global::Blueprint.HttpClient.CallOptions? options, ");
        sb.Append("global::System.Threading.CancellationToken cancellationToken = default");
        sb.AppendLine(")");
        sb.AppendLine("        {");

        if (isResourceEvent)
        {
            sb.AppendLine($"            return client.SendResourceEventEnvelopeAsync<{op.ResourceEventResourceTypeFqn}>(operation, options, cancellationToken);");
        }
        else if (hasTypedResult)
        {
            sb.AppendLine($"            return client.SendAsync<{op.ResultTypeFqn}>(operation, options, cancellationToken);");
        }
        else
        {
            sb.AppendLine("            return client.SendAsync(operation, options, cancellationToken);");
        }

        sb.AppendLine("        }");
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (char.IsLower(name[0]))
        {
            return name;
        }

        // Handle acronyms (e.g. "ID" → "id", "URL" → "url")
        if (name.Length > 1 && char.IsUpper(name[1]))
        {
            var i = 0;
            while (i < name.Length && char.IsUpper(name[i]))
            {
                i++;
            }

            if (i == name.Length)
            {
                return name.ToLowerInvariant();
            }

            return name[..(i - 1)].ToLowerInvariant() + name[(i - 1)..];
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypesFromAssembly(IAssemblySymbol assembly)
    {
        return GetAllTypes(assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(child))
            {
                yield return type;
            }
        }
    }

    private static class Stages
    {
        /// <summary>
        /// Tracks the initial extraction of app class and operation metadata for typed client generation.
        /// </summary>
        public const string InitialExtraction = nameof(InitialExtraction);
    }
}

/// <summary>
/// Compact model for a Blueprint app's typed HTTP client extension generation.
/// </summary>
internal sealed record TypedClientModel(
    string? AppNamespace,
    string AppClassName,
    string AppFqn,
    EquatableList<TypedClientOperationModel> Operations);

/// <summary>
/// Per-operation metadata for typed client method generation.
/// </summary>
/// <param name="OperationFqn">Fully-qualified name of the operation type.</param>
/// <param name="OperationTypeName">Simple name of the operation type.</param>
/// <param name="MethodName">Name of the generated extension method.</param>
/// <param name="ResultTypeFqn">FQN of the result type (null for void operations).</param>
/// <param name="IsResultValueType">Whether the result type is a value type.</param>
/// <param name="HttpMethod">HTTP method (GET, POST, etc.).</param>
/// <param name="Parameters">The operation's public settable properties as method parameters.</param>
/// <param name="ResourceEventResourceTypeFqn">
/// When the result type is <c>ResourceEvent&lt;T&gt;</c> (or a subtype like <c>ResourceCreated&lt;T&gt;</c>),
/// this holds the FQN of the inner resource type <c>T</c>. The generated extension method then calls
/// <c>SendResourceEventEnvelopeAsync&lt;T&gt;</c> instead of <c>SendAsync&lt;ResourceEvent&lt;T&gt;&gt;</c>,
/// because the HTTP host serialises resource events as an envelope that requires special unwrapping.
/// </param>
internal sealed record TypedClientOperationModel(
    string OperationFqn,
    string OperationTypeName,
    string MethodName,
    string? ResultTypeFqn,
    bool IsResultValueType,
    string HttpMethod,
    EquatableList<TypedClientParameterModel> Parameters,
    string? ResourceEventResourceTypeFqn = null);

/// <summary>
/// Per-parameter metadata for typed client method generation.
/// </summary>
internal sealed record TypedClientParameterModel(
    string Name,
    string TypeFqn,
    bool IsNullable,
    bool IsRequired,
    bool IsValueType);
