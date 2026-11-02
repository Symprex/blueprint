using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.HttpClient;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// Generates a compile-time typed service client for each hand-written <c>partial class XApi</c> marked
/// <c>[BlueprintClient]</c>: an <c>IXApi</c> interface and the class's implementation of it, one method per operation named
/// by a <c>[BlueprintClientInclude(typeof(Op))]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each method sends its operation through <c>BlueprintHttpClient.SendAsync</c> with a call plan fixed here: a constant HTTP
/// method, the operation's own generated <c>Op.Http</c> builders, and the reader of its result type. So a call consults no
/// <c>BlueprintApp</c>, descriptor or runtime scan, and runs no type test or reflection.
/// </para>
/// <para>
/// Roslyn generators cannot see each other's output, so whether an operation compiled in this same compilation gets its
/// <c>Http</c> builders is predicted from what <c>HttpClientRequestBuilderModule</c> emits them for: a partial operation with
/// a route that can declare the class. An operation from a referenced assembly is read as compiled.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class BlueprintClientSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private const string ReadersType = "global::Blueprint.HttpClient.BlueprintResponseReaders";

    private static readonly SymbolDisplayFormat _fullyQualifiedWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var clients = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.HttpClient.BlueprintClientAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => Transform(ctx, ct))
            .WithTrackingName(Stages.InitialExtraction);

        // Reported against the compilation's own syntax trees, so that a consumer can suppress one with #pragma.
        context.RegisterDiagnosticOutput(clients.Select(static (client, _) => client.Diagnostics));

        context.RegisterSourceOutput(clients, static (spc, client) =>
        {
            if (client.CanGenerate)
            {
                spc.AddSource(
                    $"{(client.Namespace is null ? string.Empty : client.Namespace + ".")}{client.ClassName}_BlueprintClient.g.cs",
                    SourceText.From(Render(client), Encoding.UTF8));
            }
        });
    }

    private static ClientModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        var @namespace = classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString();
        var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        var shapeProblem = !classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword) ? "must be partial"
            : classSymbol.ContainingType is not null ? "must not be nested in another type"
            : classSymbol.IsGenericType ? "must not be generic"
            : classSymbol.IsAbstract ? "must not be abstract"
            : null;

        if (shapeProblem is not null)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.ClientNotPartial,
                classSyntax.Identifier.GetLocation(),
                new EquatableList<string>([classSymbol.Name, shapeProblem])));

            return ClientModel.Failed(@namespace, classSymbol.Name, diagnostics);
        }

        var (serializerContextFqn, serializableTypes) = ReadSerializerContext(context.Attributes[0]);
        var operations = new List<ClientOperationModel>();
        var includedOperations = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attribute in classSymbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is not { Name: "BlueprintClientIncludeAttribute" } attributeClass
                || !attributeClass.IsInNamespace("Blueprint", "HttpClient"))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            var included = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol : null;

            var operation = CreateOperation(included, location, serializerContextFqn, serializableTypes, diagnostics);

            if (operation is null)
            {
                continue;
            }

            if (!includedOperations.Add(operation.OperationFqn))
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.DuplicateInclude,
                    location,
                    new EquatableList<string>([$"The operation '{operation.OperationFqn.Replace("global::", string.Empty)}' is included more than once"])));
                continue;
            }

            if (methodNames.TryGetValue(operation.MethodName, out var existing))
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.DuplicateInclude,
                    location,
                    new EquatableList<string>([$"'{operation.OperationFqn.Replace("global::", string.Empty)}' would have the method name '{operation.MethodName}' that '{existing}' already has"])));
                continue;
            }

            methodNames.Add(operation.MethodName, operation.OperationFqn.Replace("global::", string.Empty));
            operations.Add(operation);
        }

        var copiedMethods = new List<string>();
        var readonlyFields = new List<ClientFieldModel>();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal) { "client" };

        foreach (var member in classSymbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.DeclaringSyntaxReferences.IsEmpty)
            {
                continue;
            }

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false, IsOverride: false, DeclaredAccessibility: Accessibility.Public } method:
                    if (method.IsGenericMethod)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            ClientDiagnosticDescriptors.GenericPublicMethod,
                            method.Locations[0],
                            new EquatableList<string>([method.Name])));
                    }
                    else
                    {
                        copiedMethods.Add(RenderMethodSignature(method));
                    }

                    break;

                case IFieldSymbol { IsReadOnly: true, IsStatic: false, IsConst: false } field:
                    // A field whose parameter name is empty or taken (the transport's "client", or another field's) keeps its
                    // own name, which is unique among the class's members.
                    var parameterName = ClientSymbolHelpers.ToCamelCase(field.Name.TrimStart('_'));

                    if (parameterName.Length == 0 || !parameterNames.Add(parameterName))
                    {
                        parameterName = field.Name;
                    }

                    readonlyFields.Add(new ClientFieldModel(
                        field.Type.ToDisplayString(_fullyQualifiedWithNullable),
                        ClientSymbolHelpers.EscapeIdentifier(field.Name),
                        ClientSymbolHelpers.EscapeIdentifier(parameterName)));
                    break;
            }
        }

        return new ClientModel(
            @namespace,
            classSymbol.Name,
            accessibility,
            classSymbol.IsSealed,
            CanGenerate: true,
            new EquatableList<ClientOperationModel>([.. operations]),
            new EquatableList<string>([.. copiedMethods]),
            new EquatableList<ClientFieldModel>([.. readonlyFields]),
            new EquatableList<DiagnosticInfo>([.. diagnostics]));
    }

    /// <summary>
    /// Validates one include and builds its method's model, or reports why it cannot have one.
    /// </summary>
    private static ClientOperationModel? CreateOperation(
        INamedTypeSymbol? operation,
        Location? location,
        string? serializerContextFqn,
        HashSet<string> serializableTypes,
        List<DiagnosticInfo> diagnostics)
    {
        if (operation is null
            || operation.TypeKind != TypeKind.Class
            || operation.IsAbstract
            || operation.IsGenericType
            || !operation.AllInterfaces.Any(static i => i.Name == "IOperation" && i.IsInNamespace("Blueprint", "Model")))
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.IncludeNotConcreteOperation,
                location,
                new EquatableList<string>([operation?.ToDisplayString() ?? "?"])));
            return null;
        }

        var operationName = operation.ToDisplayString();
        var resultType = ClientSymbolHelpers.FindDeclaredResultType(operation);

        if (resultType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.OperationHasNoResult,
                location,
                new EquatableList<string>([operationName])));
            return null;
        }

        if (GetMissingRequestBuilderReason(operation) is { } reason)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.OperationHasNoRequestBuilder,
                location,
                new EquatableList<string>([operationName, reason])));
            return null;
        }

        var jsonTypes = new List<ITypeSymbol>();
        var reader = CreateReader(resultType, jsonTypes);

        if (reader is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.ResultHasNoReader,
                location,
                new EquatableList<string>([resultType.ToDisplayString(), operationName])));
            return null;
        }

        // The method the descriptor registers first, which is the one BlueprintHttpClient<TApp> sends.
        var httpMethod = OperationHttpMethodMetadataModule.ResolveFirstHttpMethod(operation);

        if (httpMethod is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.OperationHasNoRequestBuilder,
                location,
                new EquatableList<string>([operationName, "has an invalid HTTP method"])));
            return null;
        }

        if (serializerContextFqn is not null)
        {
            if (httpMethod != "GET")
            {
                jsonTypes.Insert(0, operation);
            }

            foreach (var jsonType in jsonTypes)
            {
                var name = jsonType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (!serializableTypes.Contains(name))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        ClientDiagnosticDescriptors.SerializerContextMissingType,
                        location,
                        new EquatableList<string>([serializerContextFqn.Replace("global::", string.Empty), jsonType.ToDisplayString(), operationName])));
                }
            }
        }

        return new ClientOperationModel(
            operation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            operation.Name,
            GetMethodName(operation.Name),
            resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ToHttpMethodExpression(httpMethod),
            IsGet: httpMethod == "GET",
            reader,
            serializerContextFqn);
    }

    /// <summary>
    /// Why the operation has no <c>Op.Http</c> request builders, or <see langword="null"/> when it has them: an operation
    /// from a referenced assembly has them when they were compiled into it; one in this compilation will be given them by
    /// <c>HttpClientRequestBuilderModule</c> when it is partial, has a route and can declare the class.
    /// </summary>
    private static string? GetMissingRequestBuilderReason(INamedTypeSymbol operation)
    {
        if (operation.GetAttributes().Any(static a => a.AttributeClass is { Name: "UnexposedOperationAttribute" }))
        {
            return "is [UnexposedOperation]";
        }

        var http = operation.GetTypeMembers("Http").FirstOrDefault();

        if (http is not null)
        {
            return http.GetMembers("BuildUrl").Any(static m => m is IMethodSymbol { IsStatic: true })
                ? null
                : "declares its own member named Http";
        }

        if (operation.DeclaringSyntaxReferences.IsEmpty)
        {
            return "was compiled without typed request builders";
        }

        if (!operation.GetMembers("Http").IsEmpty)
        {
            return "declares its own member named Http";
        }

        if (!HttpClientRequestBuilderModule.CanDeclareHttpClass(operation))
        {
            return "derives from a concrete operation";
        }

        var isPartial = operation.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(static c => c.Modifiers.Any(SyntaxKind.PartialKeyword));

        var hasRoute = operation.GetAttributes().Any(static a =>
            a.AttributeClass is { Name: "RootLinkAttribute" or "SelfLinkAttribute" or "LinkAttribute" } attributeClass
            && attributeClass.IsInNamespace("Blueprint", "Model", "Attributes"));

        return !isPartial ? "is not partial" : hasRoute ? null : "has no route";
    }

    /// <summary>
    /// The reader expression for a declared result type, chosen as <c>BlueprintHttpClient&lt;TApp&gt;</c> chooses at run
    /// time, collecting the types it reads as JSON; or <see langword="null"/> when the result has no reader.
    /// </summary>
    private static string? CreateReader(ITypeSymbol resultType, List<ITypeSymbol> jsonTypes)
    {
        var fqn = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (resultType is INamedTypeSymbol { Name: "Results", TypeArguments.Length: 2 } union && union.IsInNamespace("Blueprint", "Model"))
        {
            var first = CreateUnionMember(union.TypeArguments[0], jsonTypes);
            var second = CreateUnionMember(union.TypeArguments[1], jsonTypes);

            return first is null || second is null ? null : $"{ReadersType}.Union({first}, {second})";
        }

        if (IsNestedStatusCodeResult(resultType))
        {
            return HasInstanceField(resultType) ? $"{ReadersType}.StatusCode({fqn}.Instance)" : null;
        }

        if (GetResourceEventFamilyReader(resultType, member: false) is { } eventReader)
        {
            jsonTypes.Add(((INamedTypeSymbol)resultType).TypeArguments[0]);
            return eventReader;
        }

        if (IsGenericResourceEvent(resultType))
        {
            return null;
        }

        if (IsGeneric(resultType, "ResourceEventResult", "Blueprint", "Runtime", "Exec"))
        {
            var content = ((INamedTypeSymbol)resultType).TypeArguments[0];
            jsonTypes.Add(content);
            return $"{ReadersType}.ResourceEventResult<{Fqn(content)}>()";
        }

        switch (resultType.SpecialType)
        {
            case SpecialType.System_String:
                return $"{ReadersType}.String()";
            case SpecialType.System_Object:
                return $"{ReadersType}.BodilessObject()";
        }

        if (GetBodilessReader(resultType) is { } bodiless)
        {
            return bodiless;
        }

        if (IsType(resultType, "PlainTextResult", "Blueprint", "Runtime", "Exec"))
        {
            return $"{ReadersType}.PlainText()";
        }

        if (IsOperationResult(resultType) && !IsResourceEvent(resultType))
        {
            return null;
        }

        jsonTypes.Add(resultType);
        return $"{ReadersType}.Json<{fqn}>()";
    }

    private static string? CreateUnionMember(ITypeSymbol memberType, List<ITypeSymbol> jsonTypes)
    {
        var fqn = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (GetResourceEventFamilyReader(memberType, member: true) is { } eventMember)
        {
            jsonTypes.Add(((INamedTypeSymbol)memberType).TypeArguments[0]);
            return eventMember;
        }

        if (IsGenericResourceEvent(memberType))
        {
            return null;
        }

        if (IsGeneric(memberType, "ResourceEventResult", "Blueprint", "Runtime", "Exec"))
        {
            var content = ((INamedTypeSymbol)memberType).TypeArguments[0];
            jsonTypes.Add(content);
            return $"{ReadersType}.ResourceEventResultMember<{Fqn(content)}>()";
        }

        if (IsType(memberType, "PlainTextResult", "Blueprint", "Runtime", "Exec"))
        {
            return $"{ReadersType}.PlainTextMember()";
        }

        if (IsType(memberType, "StatusCodeResult", "Blueprint", "Runtime", "Exec"))
        {
            return $"{ReadersType}.AnyStatusCodeMember()";
        }

        if (IsNestedStatusCodeResult(memberType))
        {
            return HasInstanceField(memberType) ? $"{ReadersType}.StatusCodeMember({fqn}.Instance)" : $"{ReadersType}.NoneMember<{fqn}>()";
        }

        if (IsType(memberType, "HttpNoContentResult", "Blueprint", "Runtime", "Http"))
        {
            return $"{ReadersType}.HttpNoContentMember()";
        }

        if (IsType(memberType, "NoResultOperationResult", "Blueprint", "Runtime", "Exec"))
        {
            return $"{ReadersType}.NoResultMember()";
        }

        if (IsType(memberType, "HttpAcceptedResult", "Blueprint", "Runtime", "Http"))
        {
            return $"{ReadersType}.HttpAcceptedMember()";
        }

        if (IsType(memberType, "KeyOnlyResourceEventResult", "Blueprint", "Runtime", "Exec"))
        {
            return $"{ReadersType}.KeyOnlyResourceEventMember()";
        }

        if (IsResourceEvent(memberType) || !IsOperationResult(memberType))
        {
            jsonTypes.Add(memberType);

            return FindResourceEventResourceType(memberType) is { } resource
                ? $"{ReadersType}.ResourceEventJsonMember<{fqn}, {Fqn(resource)}>()"
                : $"{ReadersType}.JsonMember<{fqn}>()";
        }

        foreach (var (name, factory) in new[] { ("OkResult", "OkMember"), ("CreatedResult", "CreatedMember"), ("AcceptedResult", "AcceptedMember") })
        {
            if (IsGeneric(memberType, name, "Blueprint", "Runtime", "Exec"))
            {
                var content = ((INamedTypeSymbol)memberType).TypeArguments[0];
                jsonTypes.Add(content);
                return $"{ReadersType}.{factory}<{Fqn(content)}>()";
            }
        }

        return $"{ReadersType}.NoneMember<{fqn}>()";
    }

    /// <summary>
    /// The reader of a result the host can write with no body: the base <see cref="object"/> handled by the caller, the
    /// operation-result bases, which also read plain text, and the results written as a status alone.
    /// </summary>
    private static string? GetBodilessReader(ITypeSymbol resultType)
    {
        return resultType switch
        {
            _ when IsType(resultType, "OperationResult", "Blueprint") => $"{ReadersType}.BodilessOperationResult()",
            _ when IsType(resultType, "HttpResult", "Blueprint", "Http") => $"{ReadersType}.BodilessHttpResult()",
            _ when IsType(resultType, "StatusCodeResult", "Blueprint", "Runtime", "Exec") => $"{ReadersType}.BodilessStatusCodeResult()",
            _ when IsType(resultType, "HttpNoContentResult", "Blueprint", "Runtime", "Http") => $"{ReadersType}.BodilessHttpNoContent()",
            _ when IsType(resultType, "NoResultOperationResult", "Blueprint", "Runtime", "Exec") => $"{ReadersType}.BodilessNoResult()",
            _ when IsType(resultType, "HttpAcceptedResult", "Blueprint", "Runtime", "Http") => $"{ReadersType}.BodilessHttpAccepted()",
            _ when IsType(resultType, "KeyOnlyResourceEventResult", "Blueprint", "Runtime", "Exec") => $"{ReadersType}.BodilessKeyOnlyResourceEvent()",
            _ => null,
        };
    }

    /// <summary>
    /// The reader, or union member, of exactly <c>ResourceEvent&lt;T&gt;</c>, <c>ResourceCreated&lt;T&gt;</c>,
    /// <c>ResourceUpdated&lt;T&gt;</c> or <c>ResourceDeleted&lt;T&gt;</c>, which Blueprint can make an instance of to read
    /// into; <see langword="null"/> for any other type.
    /// </summary>
    private static string? GetResourceEventFamilyReader(ITypeSymbol type, bool member)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            || !named.IsInNamespace("Blueprint", "Http")
            || named.Name is not ("ResourceEvent" or "ResourceCreated" or "ResourceUpdated" or "ResourceDeleted"))
        {
            return null;
        }

        return $"{ReadersType}.{named.Name}{(member ? "Member" : string.Empty)}<{Fqn(named.TypeArguments[0])}>()";
    }

    /// <summary>
    /// Whether the type is a generic, concrete resource event other than the four Blueprint declares, which
    /// <c>BlueprintHttpClient&lt;TApp&gt;</c> reads into an uninitialised instance and a typed client cannot make.
    /// </summary>
    private static bool IsGenericResourceEvent(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: true, IsAbstract: false } && FindResourceEventResourceType(type) is not null;
    }

    private static ITypeSymbol? FindResourceEventResourceType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current is { Name: "ResourceEvent", IsGenericType: true, TypeArguments.Length: 1 } && current.IsInNamespace("Blueprint", "Http"))
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsResourceEvent(ITypeSymbol type)
    {
        return InheritsFrom(type, "ResourceEvent", "Blueprint", "Http");
    }

    private static bool IsOperationResult(ITypeSymbol type)
    {
        return InheritsFrom(type, "OperationResult", "Blueprint");
    }

    private static bool IsNestedStatusCodeResult(ITypeSymbol type)
    {
        return !IsType(type, "StatusCodeResult", "Blueprint", "Runtime", "Exec")
               && InheritsFrom(type, "StatusCodeResult", "Blueprint", "Runtime", "Exec");
    }

    private static bool HasInstanceField(ITypeSymbol type)
    {
        return type.GetMembers("Instance").Any(m => m is IFieldSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } f
                                                    && SymbolEqualityComparer.Default.Equals(f.Type, type));
    }

    private static bool InheritsFrom(ITypeSymbol type, string name, params string[] namespaceParts)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == name && !current.IsGenericType && IsInNamespace(current, namespaceParts))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsType(ITypeSymbol type, string name, params string[] namespaceParts)
    {
        return type is INamedTypeSymbol { IsGenericType: false } named && named.Name == name && IsInNamespace(named, namespaceParts);
    }

    private static bool IsGeneric(ITypeSymbol type, string name, params string[] namespaceParts)
    {
        return type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named && named.Name == name && IsInNamespace(named, namespaceParts);
    }

    private static bool IsInNamespace(ISymbol symbol, string[] namespaceParts)
    {
        return symbol.ContainingNamespace?.ToDisplayString() == string.Join(".", namespaceParts);
    }

    private static string Fqn(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    /// The serializer context named on the attribute, and the fully-qualified names of the types it declares
    /// <c>[JsonSerializable]</c> for.
    /// </summary>
    private static (string? ContextFqn, HashSet<string> SerializableTypes) ReadSerializerContext(AttributeData clientAttribute)
    {
        var serializableTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var argument in clientAttribute.NamedArguments)
        {
            if (argument is not { Key: "SerializerContext", Value.Value: INamedTypeSymbol contextType })
            {
                continue;
            }

            foreach (var attribute in contextType.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "JsonSerializableAttribute" }
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is ITypeSymbol serializable)
                {
                    serializableTypes.Add(serializable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }

            return (contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), serializableTypes);
        }

        return (null, serializableTypes);
    }

    /// <summary>
    /// The method name for an operation: its type name, less one trailing <c>Command</c> or <c>Query</c>, with
    /// <c>Async</c> appended.
    /// </summary>
    private static string GetMethodName(string operationTypeName)
    {
        var name = operationTypeName.EndsWith("Command", StringComparison.Ordinal)
            ? operationTypeName[..^"Command".Length]
            : operationTypeName.EndsWith("Query", StringComparison.Ordinal)
                ? operationTypeName[..^"Query".Length]
                : operationTypeName;

        return name + "Async";
    }

    private static string ToHttpMethodExpression(string httpMethod)
    {
        return httpMethod switch
        {
            "GET" => "global::System.Net.Http.HttpMethod.Get",
            "POST" => "global::System.Net.Http.HttpMethod.Post",
            "PUT" => "global::System.Net.Http.HttpMethod.Put",
            "DELETE" => "global::System.Net.Http.HttpMethod.Delete",
            "PATCH" => "global::System.Net.Http.HttpMethod.Patch",
            _ => $"new global::System.Net.Http.HttpMethod(\"{httpMethod}\")",
        };
    }

    /// <summary>
    /// A public method's signature as the interface declares it: fully-qualified types, its parameters' modifiers and
    /// default values.
    /// </summary>
    private static string RenderMethodSignature(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(static p =>
        {
            var modifier = p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => p.IsParams ? "params " : string.Empty,
            };

            var defaultValue = p.HasExplicitDefaultValue ? " = " + RenderDefaultValue(p) : string.Empty;

            return $"{modifier}{p.Type.ToDisplayString(_fullyQualifiedWithNullable)} {ClientSymbolHelpers.EscapeIdentifier(p.Name)}{defaultValue}";
        });

        var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(_fullyQualifiedWithNullable);

        return $"{returnType} {ClientSymbolHelpers.EscapeIdentifier(method.Name)}({string.Join(", ", parameters)});";
    }

    private static string RenderDefaultValue(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;

        if (value is null)
        {
            return parameter.Type.IsValueType && parameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ? "default" : "null";
        }

        var enumType = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : parameter.Type;

        if (enumType.TypeKind == TypeKind.Enum)
        {
            return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(value, false, false)}";
        }

        var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(value, true, false);

        return value switch
        {
            float => literal + "F",
            decimal => literal + "M",
            long => literal + "L",
            ulong => literal + "UL",
            uint => literal + "U",
            _ => literal,
        };
    }

    private static string Render(ClientModel client)
    {
        var interfaceName = "I" + client.ClassName;
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");

        // An included operation may be [Obsolete]; including it is the consumer's choice, so naming it here is not a warning.
        sb.AppendLine("#pragma warning disable CS0612, CS0618");
        sb.AppendLine();

        var indent = string.Empty;

        if (client.Namespace is not null)
        {
            sb.AppendLine($"namespace {client.Namespace}");
            sb.AppendLine("{");
            indent = "    ";
        }

        RenderInterface(sb, indent, client, interfaceName);
        sb.AppendLine();
        RenderClass(sb, indent, client, interfaceName);

        if (client.Namespace is not null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void RenderInterface(StringBuilder sb, string indent, ClientModel client, string interfaceName)
    {
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// The typed client <see cref=\"{client.ClassName}\"/> implements, generated from its <c>[BlueprintClientInclude]</c> operations.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}{client.Accessibility} partial interface {interfaceName}");
        sb.AppendLine($"{indent}{{");

        foreach (var operation in client.Operations)
        {
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Sends a <see cref=\"{operation.OperationFqn}\"/> and reads its result.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    /// <param name=\"operation\">The operation to send.</param>");
            sb.AppendLine($"{indent}    /// <param name=\"options\">Per-call options, such as a request modification.</param>");
            sb.AppendLine($"{indent}    /// <param name=\"cancellationToken\">Cancellation token.</param>");
            sb.AppendLine($"{indent}    /// <returns>The operation's declared result.</returns>");
            sb.AppendLine($"{indent}    global::System.Threading.Tasks.Task<{operation.ResultFqn}> {operation.MethodName}({operation.OperationFqn} operation, global::Blueprint.HttpClient.CallOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default);");
            sb.AppendLine();
        }

        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Returns a clone that applies <paramref name=\"credential\"/> to every request, after any request modification. A later call replaces it.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine($"{indent}    /// <param name=\"credential\">The credential.</param>");
        sb.AppendLine($"{indent}    /// <returns>The clone.</returns>");
        sb.AppendLine($"{indent}    {interfaceName} WithCredential(global::Blueprint.HttpClient.IUpstreamCredential credential);");

        foreach (var method in client.CopiedMethods)
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Copied from the public method of <see cref=\"{client.ClassName}\"/>.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    {method}");
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void RenderClass(StringBuilder sb, string indent, ClientModel client, string interfaceName)
    {
        var className = client.ClassName;
        var modifierAccessibility = client.IsSealed ? "private" : "protected";
        const string ClientType = "global::Blueprint.HttpClient.BlueprintHttpClient";
        const string ModifierType = "global::System.Action<global::System.Net.Http.HttpRequestMessage>";
        const string CredentialType = "global::Blueprint.HttpClient.IUpstreamCredential";

        sb.AppendLine($"{indent}{client.Accessibility} partial class {className} : {interfaceName}");
        sb.AppendLine($"{indent}{{");

        foreach (var operation in client.Operations)
        {
            sb.AppendLine($"{indent}    private static readonly global::Blueprint.HttpClient.BlueprintCallPlan<{operation.OperationFqn}, {operation.ResultFqn}> _{ToFieldName(operation.MethodName)}Plan = new(");
            sb.AppendLine($"{indent}        \"{operation.OperationName}\",");
            sb.AppendLine($"{indent}        {operation.HttpMethodExpression},");
            sb.AppendLine($"{indent}        {operation.OperationFqn}.Http.BuildUrl,");
            sb.AppendLine($"{indent}        {(operation.IsGet ? operation.OperationFqn + ".Http.AppendQueryString" : "null")},");
            sb.AppendLine($"{indent}        {operation.ReaderExpression},");
            sb.AppendLine($"{indent}        {(operation.SerializerContextFqn is null ? "null" : operation.SerializerContextFqn + ".Default")});");
            sb.AppendLine();
        }

        sb.AppendLine($"{indent}    private readonly {ClientType} _blueprintClient;");
        sb.AppendLine($"{indent}    private readonly {ModifierType}? _blueprintRequestModifier;");
        sb.AppendLine($"{indent}    private readonly {CredentialType}? _blueprintCredential;");
        sb.AppendLine();

        // The public constructor: the transport, then each instance readonly field of the partial.
        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Initialises a new instance of the <see cref=\"{className}\"/> class.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine($"{indent}    /// <param name=\"client\">The transport the calls are sent through.</param>");

        foreach (var field in client.ReadonlyFields)
        {
            sb.AppendLine($"{indent}    /// <param name=\"{field.ParameterName.TrimStart('@')}\">The value of <c>{field.FieldName.TrimStart('@')}</c>.</param>");
        }

        var parameters = string.Concat(client.ReadonlyFields.Select(static f => $", {f.TypeFqn} {f.ParameterName}"));
        sb.AppendLine($"{indent}    public {className}({ClientType} client{parameters})");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        this._blueprintClient = client ?? throw new global::System.ArgumentNullException(nameof(client));");

        foreach (var field in client.ReadonlyFields)
        {
            sb.AppendLine($"{indent}        this.{field.FieldName} = {field.ParameterName};");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        // The cloning constructor behind WithCredential and WithRequestModifier.
        sb.AppendLine($"{indent}    private {className}({className} source, {ModifierType}? requestModifier, {CredentialType}? credential)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        this._blueprintClient = source._blueprintClient;");

        foreach (var field in client.ReadonlyFields)
        {
            sb.AppendLine($"{indent}        this.{field.FieldName} = source.{field.FieldName};");
        }

        sb.AppendLine($"{indent}        this._blueprintRequestModifier = requestModifier;");
        sb.AppendLine($"{indent}        this._blueprintCredential = credential;");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        sb.AppendLine($"{indent}    /// <inheritdoc />");
        sb.AppendLine($"{indent}    public {interfaceName} WithCredential({CredentialType} credential)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        global::System.ArgumentNullException.ThrowIfNull(credential);");
        sb.AppendLine();
        sb.AppendLine($"{indent}        return new {className}(this, this._blueprintRequestModifier, credential);");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Returns a clone that applies <paramref name=\"requestModifier\"/> to every request, after the caller's");
        sb.AppendLine($"{indent}    /// <see cref=\"global::Blueprint.HttpClient.CallOptions.ModifyRequest\"/> and any modifier added before it, and before");
        sb.AppendLine($"{indent}    /// the credential.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine($"{indent}    /// <param name=\"requestModifier\">The modification.</param>");
        sb.AppendLine($"{indent}    /// <returns>The clone.</returns>");
        sb.AppendLine($"{indent}    {modifierAccessibility} {interfaceName} WithRequestModifier({ModifierType} requestModifier)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        global::System.ArgumentNullException.ThrowIfNull(requestModifier);");
        sb.AppendLine();
        sb.AppendLine($"{indent}        var existing = this._blueprintRequestModifier;");
        sb.AppendLine();
        sb.AppendLine($"{indent}        return new {className}(");
        sb.AppendLine($"{indent}            this,");
        sb.AppendLine($"{indent}            existing is null ? requestModifier : request => {{ existing(request); requestModifier(request); }},");
        sb.AppendLine($"{indent}            this._blueprintCredential);");
        sb.AppendLine($"{indent}    }}");

        foreach (var operation in client.Operations)
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Configures the call options of a <see cref=\"{operation.OperationFqn}\"/> before it is sent.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    /// <param name=\"operation\">The operation about to be sent.</param>");
            sb.AppendLine($"{indent}    /// <param name=\"options\">The caller's options, which the implementation may replace.</param>");
            sb.AppendLine($"{indent}    partial void OnConfigure({operation.OperationFqn} operation, ref global::Blueprint.HttpClient.CallOptions? options);");
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <inheritdoc />");
            sb.AppendLine($"{indent}    public global::System.Threading.Tasks.Task<{operation.ResultFqn}> {operation.MethodName}({operation.OperationFqn} operation, global::Blueprint.HttpClient.CallOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        global::System.ArgumentNullException.ThrowIfNull(operation);");
            sb.AppendLine();
            sb.AppendLine($"{indent}        this.OnConfigure(operation, ref options);");
            sb.AppendLine();
            sb.AppendLine($"{indent}        return this._blueprintClient.SendAsync(_{ToFieldName(operation.MethodName)}Plan, operation, options, this._blueprintRequestModifier, this._blueprintCredential, cancellationToken);");
            sb.AppendLine($"{indent}    }}");
        }

        sb.AppendLine($"{indent}}}");
    }

    private static string ToFieldName(string methodName)
    {
        return char.ToLowerInvariant(methodName[0]) + methodName[1..];
    }

    /// <summary>
    /// A typed client to generate, with no symbol or location in it so that it caches.
    /// </summary>
    /// <param name="Namespace">The class's namespace, or <see langword="null"/> for the global namespace.</param>
    /// <param name="ClassName">The class's name.</param>
    /// <param name="Accessibility">The class's accessibility keyword, which the interface takes too.</param>
    /// <param name="IsSealed">Whether the class is sealed, which makes <c>WithRequestModifier</c> private.</param>
    /// <param name="CanGenerate">Whether anything can be generated into the class.</param>
    /// <param name="Operations">The included operations that can have a method.</param>
    /// <param name="CopiedMethods">The interface signatures of the class's public methods.</param>
    /// <param name="ReadonlyFields">The class's instance readonly fields.</param>
    /// <param name="Diagnostics">The diagnostics to report.</param>
    private sealed record ClientModel(
        string? Namespace,
        string ClassName,
        string Accessibility,
        bool IsSealed,
        bool CanGenerate,
        EquatableList<ClientOperationModel> Operations,
        EquatableList<string> CopiedMethods,
        EquatableList<ClientFieldModel> ReadonlyFields,
        EquatableList<DiagnosticInfo> Diagnostics)
    {
        public static ClientModel Failed(string? @namespace, string className, List<DiagnosticInfo> diagnostics)
        {
            return new ClientModel(
                @namespace,
                className,
                "public",
                IsSealed: false,
                CanGenerate: false,
                EquatableList<ClientOperationModel>.Empty,
                EquatableList<string>.Empty,
                EquatableList<ClientFieldModel>.Empty,
                new EquatableList<DiagnosticInfo>([.. diagnostics]));
        }
    }

    /// <summary>
    /// One included operation's method and call plan.
    /// </summary>
    /// <param name="OperationFqn">The operation's fully-qualified name.</param>
    /// <param name="OperationName">The operation's type name.</param>
    /// <param name="MethodName">The client method's name.</param>
    /// <param name="ResultFqn">The declared result's fully-qualified name.</param>
    /// <param name="HttpMethodExpression">The HTTP method, as an expression.</param>
    /// <param name="IsGet">Whether the operation is sent as a GET, with a query string.</param>
    /// <param name="ReaderExpression">The reader of the result, as an expression.</param>
    /// <param name="SerializerContextFqn">The client's serializer context, if any.</param>
    private sealed record ClientOperationModel(
        string OperationFqn,
        string OperationName,
        string MethodName,
        string ResultFqn,
        string HttpMethodExpression,
        bool IsGet,
        string ReaderExpression,
        string? SerializerContextFqn);

    /// <summary>
    /// An instance readonly field of the client class, which becomes a constructor parameter.
    /// </summary>
    /// <param name="TypeFqn">The field's type.</param>
    /// <param name="FieldName">The field's name, escaped.</param>
    /// <param name="ParameterName">The constructor parameter's name, escaped.</param>
    private sealed record ClientFieldModel(string TypeFqn, string FieldName, string ParameterName);

    /// <summary>
    /// The names of the generator's stages, which the cacheability tests track.
    /// </summary>
    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
    }
}
