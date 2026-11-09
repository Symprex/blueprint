using System.Collections.Immutable;
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
/// Completes a hand-written <c>partial</c> class marked <c>[BlueprintClient]</c> into a typed client of a remote Blueprint
/// API: an <c>I{Class}</c> interface with one method per <c>[BlueprintClientInclude(typeof(Op))]</c>, and the class's
/// implementation of it over a <c>BlueprintHttpClient</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each method is <c>Task&lt;R&gt; {Name}Async(Op operation, CallOptions? options = null, CancellationToken cancellationToken =
/// default)</c>, the shape Signature365's generated service clients have, where <c>R</c> is the result the operation declares.
/// Its body is a plan settled at compile time: the operation's HTTP method as a constant, its URL from the request builders
/// generated onto the operation itself (<c>Op.Http</c>), and a reader chosen for <c>R</c> here. The JSON metadata the body is
/// written with and each reader reads with is resolved when a client is made, once per JSON options instance, into a plan
/// every client over those options shares. So a call looks up no app and no descriptor, and no metadata but in two cases. A
/// body's operation is compared with its declared type, and an instance of a derived type is written as its run-time type,
/// with the options' metadata for that type, because the declared metadata would drop the derived type's own members. And a
/// type whose metadata could not be resolved into the plan (BPCL007) is read or written with the options' own.
/// </para>
/// <para>
/// The reader chosen for a result is the reader <c>BlueprintHttpClient&lt;TApp&gt;</c> chooses for it at run time, so the two
/// clients read every response identically; and the method and URL are those that client sends, so both send it identically.
/// </para>
/// <para>
/// An include the client cannot call — an operation with no declared result, no request builders, or a result no reader
/// reads — is reported, at the include, and left out rather than generated into code that does not compile.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class TypedClientGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private const string BlueprintClientAttributeMetadataName = "Blueprint.HttpClient.BlueprintClientAttribute";

    /// <summary>
    /// The nested class <c>HttpClientRequestBuilderModule</c> emits an operation's request builders into.
    /// </summary>
    private const string HttpClassName = "Http";
    private const string PlanResolveName = "TryResolveTypeInfo";

    private const string WithCredentialMethodName = "WithCredential";

    private const string WithRequestModifierMethodName = "WithRequestModifier";

    private const string CreateMethodName = "Create";

    private const string ReadersNamespace = "global::Blueprint.HttpClient.Readers.";

    private const string ClientFieldName = "_client";
    private const string PlanFieldName = "_plan";
    private const string CredentialFieldName = "_credential";
    private const string RequestModifierFieldName = "_requestModifier";

    /// <summary>
    /// The nested class of the generated half that holds the client's plan.
    /// </summary>
    private const string PlanClassName = "BlueprintClientPlan";

    /// <summary>
    /// What the generated half declares in the class by a fixed name, and what each is, which no hand-written member of any
    /// kind can be named: the compiler would refuse the two as a duplicate.
    /// </summary>
    private static readonly Dictionary<string, string> _generatedMemberNames = new(StringComparer.Ordinal)
    {
        [ClientFieldName] = "a field",
        [PlanFieldName] = "a field",
        [CredentialFieldName] = "a field",
        [RequestModifierFieldName] = "a field",
        [PlanClassName] = "a nested type",
        [WithCredentialMethodName] = "a method",
        [WithRequestModifierMethodName] = "a method",
    };

    /// <summary>
    /// What the generated constructors name their own parameters, which a field's parameter must not be named after.
    /// </summary>
    private static readonly HashSet<string> _reservedParameterNames = new(StringComparer.Ordinal) { "client", "source", "credential", "requestModifier", "modify" };

    /// <summary>
    /// <see cref="OperationClientShape.FullyQualifiedWithNullable"/> as a parameter is declared: with its type, name, default
    /// value and <c>params</c>, <c>ref</c>, <c>out</c> and <c>in</c> modifiers, a keyword name escaped.
    /// </summary>
    private static readonly SymbolDisplayFormat _parameterFormat = OperationClientShape.FullyQualifiedWithNullable
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                BlueprintClientAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => Extract(ctx, ct))
            .WithTrackingName(Stages.ClientExtraction);

        context.RegisterDiagnosticOutput(results.Select(static (result, _) => result.Diagnostics));

        context.RegisterSourceOutput(
            results.Where(static result => result.Client is not null),
            static (spc, result) => spc.AddSource(result.Client!.HintName, SourceText.From(Generate(result.Client), Encoding.UTF8)));
    }

    private static TypedClientResult Extract(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (context.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } clientType
            || context.TargetNode is not ClassDeclarationSyntax classDeclaration)
        {
            return new TypedClientResult(null, EquatableList<DiagnosticInfo>.Empty);
        }

        // Only a partial, top-level, non-generic, non-static class can be completed by a generated half, so nothing is
        // generated for one that is not; the include and member checks below would only be follow-on noise for it.
        var problems = DescribeUnsupportedClass(clientType, classDeclaration);

        if (problems.Count > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.ClientClassNotSupported,
                classDeclaration.Identifier.GetLocation(),
                [clientType.Name, string.Join(", ", problems)]));

            return new TypedClientResult(null, EquatableList<DiagnosticInfo>.TakeOwnership(diagnostics));
        }

        // Every other problem leaves out only what it concerns, so the client the class still describes is generated and the
        // class's own uses of it keep compiling.
        var compilation = context.SemanticModel.Compilation;
        var operationInterface = compilation.GetTypeByMetadataName("Blueprint.Model.IOperation");
        var serializerContext = FindSerializerContext(context.Attributes);
        var operations = new List<TypedClientOperationModel>();
        var includedOperations = new HashSet<string>(StringComparer.Ordinal);
        var methodOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attribute in clientType.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is not { Name: "BlueprintClientIncludeAttribute" } attributeClass
                || !attributeClass.IsInNamespace("Blueprint", "HttpClient")
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not ITypeSymbol { TypeKind: not TypeKind.Error } includedType
                || operationInterface is null)
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            var includeName = includedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if (!includedOperations.Add(includedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.IncludeDuplicated, location, [includeName, clientType.Name, "it is already included"]));
                continue;
            }

            if (includedType is not INamedTypeSymbol operationType)
            {
                diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.IncludeNotAnOperation, location, [includeName, clientType.Name]));
                continue;
            }

            if (BuildOperation(operationType, operationInterface, compilation, clientType.Name, serializerContext, location, diagnostics, cancellationToken) is not { } operation)
            {
                continue;
            }

            if (methodOwners.TryGetValue(operation.MethodName, out var owner))
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.IncludeDuplicated,
                    location,
                    [includeName, clientType.Name, $"it produces the method '{operation.MethodName}', which '{owner}' already produces"]));
                continue;
            }

            methodOwners.Add(operation.MethodName, operation.OperationName);
            operations.Add(operation);
        }

        var fields = new List<TypedClientFieldModel>();
        var fieldTypes = new List<ITypeSymbol>();
        var methods = new List<TypedClientMethodModel>();

        foreach (var member in clientType.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!member.IsImplicitlyDeclared && _generatedMemberNames.TryGetValue(member.Name, out var generated))
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.MemberConflictsWithGenerated,
                    member.Locations.FirstOrDefault(),
                    [member.Name, clientType.Name, $"the generated half declares {generated} of that name"]));
                continue;
            }

            switch (member)
            {
                case IFieldSymbol field when BuildField(field, clientType.Name, diagnostics, cancellationToken) is { } fieldModel:
                    fields.Add(fieldModel);
                    fieldTypes.Add(field.Type);
                    break;
                case IMethodSymbol method when BuildMethod(method, clientType.Name, methodOwners, diagnostics) is { } methodModel:
                    methods.Add(methodModel);
                    break;
            }
        }

        ReportConstructorsOfGeneratedSignatures(clientType, compilation, fieldTypes, diagnostics);

        var client = new TypedClientModel(
            clientType.ContainingNamespace.IsGlobalNamespace ? null : clientType.ContainingNamespace.ToDisplayString(),
            clientType.Name,
            clientType.DeclaredAccessibility == Accessibility.Public,
            EquatableList<TypedClientOperationModel>.TakeOwnership(operations),
            EquatableList<TypedClientFieldModel>.TakeOwnership(fields),
            EquatableList<TypedClientMethodModel>.TakeOwnership(methods),
            clientType.IsSealed,
            serializerContext?.Fqn);

        return new TypedClientResult(client, EquatableList<DiagnosticInfo>.TakeOwnership(diagnostics));
    }

    /// <summary>
    /// Reads the serializer context a <c>[BlueprintClient]</c> names and the types it covers, as the app's own does: the types
    /// its <c>[JsonSerializable]</c> attributes name, wherever the context declares them, a referenced assembly's included.
    /// </summary>
    /// <returns>The context, or <see langword="null"/> when the attribute names none.</returns>
    private static SerializerContextInfo? FindSerializerContext(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key != "SerializerContext" || argument.Value.Value is not INamedTypeSymbol { TypeKind: not TypeKind.Error } contextType)
                {
                    continue;
                }

                var roots = contextType.GetAttributes()
                    .Where(static a => a.AttributeClass is { Name: "JsonSerializableAttribute" } c && c.IsInNamespace("System", "Text", "Json", "Serialization"))
                    .Select(static a => a.ConstructorArguments.FirstOrDefault().Value)
                    .OfType<ITypeSymbol>();

                return new SerializerContextInfo(
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    contextType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    CollectCoveredTypes(roots));
            }
        }

        return null;
    }

    /// <summary>
    /// The types a context covers: those its <c>[JsonSerializable]</c> attributes name, and every type reachable from them,
    /// which the System.Text.Json source generator gives the context metadata for as well. A type is reached through its
    /// public instance properties and fields, its base types', a collection's or array's elements, and its generic type
    /// arguments, which cover a <see cref="Nullable{T}"/>'s underlying type.
    /// </summary>
    /// <remarks>
    /// A framework type's members are not walked, as its metadata is a primitive's or a converter's, which walks none; its
    /// generic arguments and elements still are.
    /// </remarks>
    /// <param name="roots">The types the context's attributes name.</param>
    /// <returns>The fully qualified names of the types covered.</returns>
    private static ImmutableHashSet<string> CollectCoveredTypes(IEnumerable<ITypeSymbol> roots)
    {
        var covered = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var pending = new Stack<ITypeSymbol>(roots);

        while (pending.Count > 0)
        {
            var type = pending.Pop();

            if (type.TypeKind is TypeKind.Error or TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic
                || !covered.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                continue;
            }

            if (type is IArrayTypeSymbol array)
            {
                pending.Push(array.ElementType);
                continue;
            }

            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            foreach (var argument in named.TypeArguments)
            {
                pending.Push(argument);
            }

            // A collection's metadata is its elements', which IEnumerable<T> names even where they are no type argument.
            if (named.AllInterfaces.FirstOrDefault(static i => i is { Name: "IEnumerable", Arity: 1 } && i.IsInNamespace("System", "Collections", "Generic")) is { } enumerable)
            {
                pending.Push(enumerable.TypeArguments[0]);
                continue;
            }

            if (named.ContainingNamespace is { } ns && (IsRootNamespace(ns, "System") || IsRootNamespace(ns, "Microsoft")))
            {
                continue;
            }

            for (var current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    switch (member)
                    {
                        case IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } property:
                            pending.Push(property.Type);
                            break;
                        case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field:
                            pending.Push(field.Type);
                            break;
                    }
                }
            }
        }

        return covered.ToImmutable();

        static bool IsRootNamespace(INamespaceSymbol ns, string name)
        {
            while (ns.ContainingNamespace is { IsGlobalNamespace: false } parent)
            {
                ns = parent;
            }

            return ns.Name == name;
        }
    }

    /// <summary>
    /// Says why a generated half cannot complete the class, each reason as it follows "it".
    /// </summary>
    private static List<string> DescribeUnsupportedClass(INamedTypeSymbol clientType, ClassDeclarationSyntax classDeclaration)
    {
        var problems = new List<string>();

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            problems.Add("is not declared partial");
        }

        if (clientType.ContainingType is not null)
        {
            problems.Add("is nested in another type");
        }

        if (clientType.IsGenericType)
        {
            problems.Add("is generic");
        }

        if (clientType.IsStatic)
        {
            problems.Add("is static");
        }

        if (clientType.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            problems.Add("is neither public nor internal");
        }

        return problems;
    }

    /// <summary>
    /// Reports a hand-written constructor taking the parameters of one the generated half declares: the one a client is made
    /// with, over the transport and then each readonly field the constructor is given, or the one its clones are made with.
    /// Parameter names and nullability do not tell two constructors apart, so only the types are compared.
    /// </summary>
    private static void ReportConstructorsOfGeneratedSignatures(INamedTypeSymbol clientType, Compilation compilation, List<ITypeSymbol> fieldTypes, List<DiagnosticInfo> diagnostics)
    {
        var transport = compilation.GetTypeByMetadataName("Blueprint.HttpClient.BlueprintHttpClient");
        var credential = compilation.GetTypeByMetadataName("Blueprint.HttpClient.IUpstreamCredential");
        var request = compilation.GetTypeByMetadataName("System.Net.Http.HttpRequestMessage");
        var modifier = request is null ? null : compilation.GetTypeByMetadataName("System.Action`1")?.Construct(request);

        if (transport is null || credential is null || modifier is null)
        {
            return;
        }

        ITypeSymbol[] constructing = [transport, .. fieldTypes];
        ITypeSymbol[] cloning = [clientType, credential, modifier];

        foreach (var constructor in clientType.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
            {
                continue;
            }

            var parameterTypes = constructor.Parameters.Select(static p => p.Type).ToArray();
            var conflict = HasTypes(parameterTypes, constructing)
                ? "the generated half declares the constructor a client is made with, over the transport and then each readonly field"
                : HasTypes(parameterTypes, cloning)
                    ? "the generated half declares the constructor its clones are made with"
                    : null;

            if (conflict is not null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.MemberConflictsWithGenerated,
                    constructor.Locations.FirstOrDefault(),
                    [clientType.Name, clientType.Name, conflict]));
            }
        }

        static bool HasTypes(ITypeSymbol[] parameterTypes, ITypeSymbol[] expected)
        {
            return parameterTypes.Length == expected.Length
                   && parameterTypes.Zip(expected, static (actual, wanted) => SymbolEqualityComparer.Default.Equals(actual, wanted)).All(static same => same);
        }
    }

    /// <summary>
    /// A readonly instance field the hand-written class declares without a value, which its constructor is given instead. One
    /// whose parameter would be named after a generated parameter is reported.
    /// </summary>
    private static TypedClientFieldModel? BuildField(IFieldSymbol field, string clientName, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        if (field.IsImplicitlyDeclared)
        {
            return null;
        }

        if (field is not { IsStatic: false, IsReadOnly: true, IsConst: false }
            || field.Type.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer
            || field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer: null })
        {
            return null;
        }

        var parameterName = field.Name.TrimStart('_');

        if (parameterName.Length == 0)
        {
            return null;
        }

        parameterName = char.ToLowerInvariant(parameterName[0]) + parameterName[1..];

        if (_reservedParameterNames.Contains(parameterName))
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.MemberConflictsWithGenerated,
                field.Locations.FirstOrDefault(),
                [field.Name, clientName, $"the constructor parameter it is given by would be named '{parameterName}', which the generated half uses; rename the field"]));
            return null;
        }

        if (SyntaxFacts.GetKeywordKind(parameterName) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(parameterName) != SyntaxKind.None)
        {
            parameterName = "@" + parameterName;
        }

        // A nullable value type is resolved as its underlying type, which is what a container registers it as.
        var resolveType = field.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : field.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        return new TypedClientFieldModel(
            field.Name,
            parameterName,
            field.Type.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable),
            field.Type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated },
            resolveType.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable),
            field.Type.NullableAnnotation == NullableAnnotation.Annotated,
            field.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });
    }

    /// <summary>
    /// A public, non-generic instance method the hand-written class declares, which its interface repeats. A generic one
    /// cannot be repeated, and one named as a method the generated half declares clashes with it; both are reported.
    /// </summary>
    private static TypedClientMethodModel? BuildMethod(IMethodSymbol method, string clientName, Dictionary<string, string> operationMethods, List<DiagnosticInfo> diagnostics)
    {
        if (method is not { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsOverride: false, IsImplicitlyDeclared: false })
        {
            return null;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.GenericMethodNotSupported, method.Locations.FirstOrDefault(), [method.Name, clientName]));
            return null;
        }

        if (operationMethods.TryGetValue(method.Name, out var operationName))
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.MemberConflictsWithGenerated,
                method.Locations.FirstOrDefault(),
                [method.Name, clientName, $"the generated half declares a method of that name for '{operationName}'"]));
            return null;
        }

        // The return type is repeated as declared: one written as the generated interface is not yet a type of this compilation,
        // and displays as it was written.
        var returnType = method.ReturnType.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable);
        var parameters = string.Join(", ", method.Parameters.Select(static p => p.ToDisplayString(_parameterFormat)));

        return new TypedClientMethodModel(method.Name, $"{returnType} {method.Name}({parameters})");
    }

    /// <summary>
    /// Builds the call an included operation is made through, or reports why it has none, at the include, and returns
    /// <see langword="null"/>.
    /// </summary>
    private static TypedClientOperationModel? BuildOperation(
        INamedTypeSymbol operationType,
        INamedTypeSymbol operationInterface,
        Compilation compilation,
        string clientName,
        SerializerContextInfo? serializerContext,
        Location? location,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var operationName = operationType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // A concrete, non-generic operation class, as the descriptor generator generates descriptors and builders for.
        if (operationType is not { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsGenericType: false }
            || !operationType.IsAssignableTo(operationInterface))
        {
            diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.IncludeNotAnOperation, location, [operationName, clientName]));
            return null;
        }

        if (OperationClientShape.FindDeclaredResultType(operationType) is not { } resultType)
        {
            diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.IncludeHasNoResult, location, [operationName, clientName]));
            return null;
        }

        // The method the operation's descriptor publishes first, which the app-driven client sends; an attribute that declares an
        // empty one is reported by OperationHttpMethodMetadataModule, which this leaves to it.
        if (OperationHttpMethodResolver.Resolve(operationType).Methods.FirstOrDefault() is not { } httpMethod)
        {
            return null;
        }

        if (FindMissingRequestBuildersReason(operationType, compilation, appendsQueryString: httpMethod == "GET", cancellationToken) is { } reason)
        {
            diagnostics.Add(new DiagnosticInfo(ClientDiagnosticDescriptors.IncludeHasNoRequestBuilders, location, [operationName, clientName, reason]));
            return null;
        }

        var jsonTypes = new List<ITypeSymbol>();

        if (CreateReader(resultType, jsonTypes) is not { } reader)
        {
            diagnostics.Add(new DiagnosticInfo(
                ClientDiagnosticDescriptors.ResultHasNoReader,
                location,
                [operationName, clientName, resultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return null;
        }

        // A GET sends no body, so its request is never written, and is no type its context must cover. A type the context does
        // not cover is still called through, by the options' own metadata; it is a warning for that reason.
        if (serializerContext is not null)
        {
            if (httpMethod != "GET")
            {
                jsonTypes.Insert(0, operationType);
            }

            var missing = jsonTypes
                .Where(type => !serializerContext.CoveredTypes.Contains(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .Select(static type => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (missing.Length > 0)
            {
                diagnostics.Add(new DiagnosticInfo(
                    ClientDiagnosticDescriptors.SerializerContextMissingType,
                    location,
                    [serializerContext.Name, clientName, string.Join(", ", missing), operationName]));
            }
        }

        return new TypedClientOperationModel(
            operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            operationType.Name,
            ToMethodName(operationType.Name),
            resultType.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable),
            httpMethod,
            reader);
    }

    /// <summary>
    /// The method an operation is called through: its type name with one trailing <c>Command</c> or <c>Query</c> removed,
    /// then <c>Async</c>.
    /// </summary>
    private static string ToMethodName(string operationName)
    {
        var name = operationName.EndsWith("Command", StringComparison.Ordinal)
            ? operationName[..^"Command".Length]
            : operationName.EndsWith("Query", StringComparison.Ordinal)
                ? operationName[..^"Query".Length]
                : operationName;

        return name + "Async";
    }

    /// <summary>
    /// Says why the operation has not the request builders a call is built from, or <see langword="null"/> when it has them:
    /// <c>Http.BuildUrl</c>, and for a GET <c>Http.AppendQueryString</c>, which <c>HttpClientRequestBuilderModule</c> emits
    /// into the operation's own partial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An operation of a referenced assembly carries them as metadata, so they are looked for. One of this compilation is
    /// given them by the descriptor generator in this same build, whose output no other generator can see, so they are
    /// expected wherever that module emits them: on a partial, top-level operation with a URL that
    /// <c>HttpClientRequestBuilderModule.CanDeclareHttpClass</c> allows, so not one named <c>Http</c> or declaring or
    /// inheriting a visible member named <c>Http</c> (the module emits a GET's appender whatever its properties).
    /// </para>
    /// <para>
    /// A derived operation is checked for builders of its own, never a base operation's, which would build the base's URL.
    /// An unexposed operation has them, and is called like any other: it is hidden from the published contract, but still
    /// routed, so that other services can reach it.
    /// </para>
    /// </remarks>
    private static string? FindMissingRequestBuildersReason(INamedTypeSymbol operationType, Compilation compilation, bool appendsQueryString, CancellationToken cancellationToken)
    {
        if (SymbolEqualityComparer.Default.Equals(operationType.ContainingAssembly, compilation.Assembly))
        {
            if (operationType.ContainingType is not null)
            {
                return "it is nested in another type, and request builders are only generated for top-level operations";
            }

            if (!HttpClientRequestBuilderModule.CanDeclareHttpClass(operationType, compilation))
            {
                return $"its name, a member it declares or inherits, or a simple name its code binds to something else, is '{HttpClassName}', which its request builders are generated into";
            }

            if (HttpHostExposureHelper.GetAllRouteTemplates(operationType).Count == 0)
            {
                return "it has no URL, so there is nothing to build a request to";
            }

            return operationType.DeclaringSyntaxReferences.Any(r => r.GetSyntax(cancellationToken) is ClassDeclarationSyntax declaration
                                                                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                ? null
                : "it is not declared partial, so its request builders cannot be generated into it";
        }

        var http = operationType.GetTypeMembers(HttpClassName)
            .FirstOrDefault(static t => t is { IsStatic: true, DeclaredAccessibility: Accessibility.Public });

        return http is not null
               && HasBuilder(http, "BuildUrl", operationType, takesUrl: false)
               && (!appendsQueryString || HasBuilder(http, "AppendQueryString", operationType, takesUrl: true))
            ? null
            : $"its assembly '{operationType.ContainingAssembly.Name}' carries no request builders for it, so it was not built with Blueprint's source generator";
    }

    private static bool HasBuilder(INamedTypeSymbol http, string name, INamedTypeSymbol operationType, bool takesUrl)
    {
        return http.GetMembers(name).OfType<IMethodSymbol>().Any(m =>
            m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, ReturnType.SpecialType: SpecialType.System_String }
            && m.Parameters.Length == (takesUrl ? 2 : 1)
            && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, operationType)
            && (!takesUrl || m.Parameters[1].Type.SpecialType == SpecialType.System_String));
    }

    /// <summary>
    /// Chooses the reader of a declared result, as <c>AppResponseReaders</c> chooses it for <c>BlueprintHttpClient&lt;TApp&gt;</c>
    /// at run time, the first that applies: a nested <c>StatusCodeResult</c> by its singleton; a <c>Results&lt;T1, T2&gt;</c> by
    /// its members' readers; the bodiless reader for <c>StatusCodeResult</c>, <c>HttpResult</c>, <c>OperationResult</c> and
    /// <c>object</c>; <c>PlainTextResult</c>; a generic resource event; <c>ResourceEventResult&lt;T&gt;</c>; an
    /// <c>OkResult&lt;T&gt;</c>, <c>CreatedResult&lt;T&gt;</c> or <c>AcceptedResult&lt;T&gt;</c> by its content; and JSON for
    /// anything else, <c>string</c> included.
    /// </summary>
    /// <param name="resultType">The declared result.</param>
    /// <param name="jsonTypes">The types the readers chosen read as JSON, which a serializer context must cover.</param>
    /// <param name="isUnionMember">
    /// Whether the result is a member of a <c>Results&lt;T1, T2&gt;</c>, whose content a content result is read from, rather than
    /// the whole declared result, which a content result is read as.
    /// </param>
    /// <returns>
    /// The expression constructing the reader in the client's plan, each reader given the metadata of what it reads as JSON
    /// from the transport the plan is made for; or <see langword="null"/> when no reader can be constructed.
    /// </returns>
    private static string? CreateReader(ITypeSymbol resultType, List<ITypeSymbol> jsonTypes, bool isUnionMember = false)
    {
        if (resultType.TypeKind is TypeKind.Error or TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic)
        {
            return null;
        }

        var resultFqn = resultType.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable);

        if (resultType.SpecialType == SpecialType.System_Object)
        {
            return $"new {ReadersNamespace}BodilessResultResponseReader<{resultFqn}>()";
        }

        if (resultType is not INamedTypeSymbol named)
        {
            jsonTypes.Add(resultType);
            return $"new {ReadersNamespace}JsonResponseReader<{resultFqn}>({TypeInfoArgument(resultType)})";
        }

        var isStatusCodeResult = IsRuntimeExecType(named, "StatusCodeResult");

        if (!isStatusCodeResult && DerivesFromStatusCodeResult(named))
        {
            // The reader is given the type's own singleton, which the response's status is matched against.
            return HasSingleton(named)
                ? $"new {ReadersNamespace}StatusCodeResultResponseReader<{resultFqn}>({resultFqn}.Instance)"
                : null;
        }

        if (named is { Name: "Results", Arity: 2 } && named.IsInNamespace("Blueprint", "Model"))
        {
            var first = CreateReader(named.TypeArguments[0], jsonTypes, isUnionMember: true);
            var second = CreateReader(named.TypeArguments[1], jsonTypes, isUnionMember: true);

            return first is null || second is null
                ? null
                : $"new {ReadersNamespace}UnionResponseReader<{named.TypeArguments[0].ToDisplayString(OperationClientShape.FullyQualifiedWithNullable)}, {named.TypeArguments[1].ToDisplayString(OperationClientShape.FullyQualifiedWithNullable)}>({first}, {second})";
        }

        if (isStatusCodeResult
            || (named is { Name: "HttpResult", Arity: 0 } && named.IsInNamespace("Blueprint", "Http"))
            || (named is { Name: "OperationResult", Arity: 0 } && named.IsInRootNamespace("Blueprint")))
        {
            return $"new {ReadersNamespace}BodilessResultResponseReader<{resultFqn}>()";
        }

        if (IsRuntimeExecType(named, "PlainTextResult"))
        {
            return $"new {ReadersNamespace}PlainTextResponseReader()";
        }

        // As the app-driven client's reader choice has it: the event reader cannot make an abstract event, so one that does
        // not declare itself polymorphic ([JsonPolymorphic] or [JsonDerivedType] on the type itself, neither being inherited)
        // can never be read; one that does is System.Text.Json's to read, as JSON.
        if (named.IsAbstract && !IsDeclaredPolymorphic(named) && OperationClientShape.FindResourceEventResourceType(named) is not null)
        {
            return null;
        }

        if (named is { IsGenericType: true, IsAbstract: false } && OperationClientShape.FindResourceEventResourceType(named) is { } resourceType)
        {
            jsonTypes.Add(resourceType);
            return $"new {ReadersNamespace}ResourceEventResponseReader<{resultFqn}, {resourceType.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable)}>({TypeInfoArgument(resourceType)})";
        }

        if (named.Arity == 1 && IsRuntimeExecType(named, "ResourceEventResult"))
        {
            jsonTypes.Add(named.TypeArguments[0]);
            return $"new {ReadersNamespace}ResourceEventResultResponseReader<{named.TypeArguments[0].ToDisplayString(OperationClientShape.FullyQualifiedWithNullable)}>({TypeInfoArgument(named.TypeArguments[0])})";
        }

        if (named.Arity == 1 && (IsRuntimeExecType(named, "OkResult") || IsRuntimeExecType(named, "CreatedResult") || IsRuntimeExecType(named, "AcceptedResult")))
        {
            var contentFqn = named.TypeArguments[0].ToDisplayString(OperationClientShape.FullyQualifiedWithNullable);

            if (isUnionMember)
            {
                // A union's member is read from its content, which is the body.
                jsonTypes.Add(named.TypeArguments[0]);
                return $"new {ReadersNamespace}ContentResultResponseReader<{resultFqn}, {contentFqn}>({TypeInfoArgument(named.TypeArguments[0])})";
            }

            // Alone, the result itself is deserialised, so it is its metadata that is resolved and its type a context must cover.
            jsonTypes.Add(resultType);
            return $"new {ReadersNamespace}ContentResultResponseReader<{resultFqn}, {contentFqn}>(resultTypeInfo: {TypeInfoArgument(resultType)})";
        }

        jsonTypes.Add(resultType);
        return $"new {ReadersNamespace}JsonResponseReader<{resultFqn}>({TypeInfoArgument(resultType)})";
    }

    /// <summary>
    /// The argument a reader is given as the metadata of a type it reads: resolved, when the plan is made, from the options of
    /// the transport it is made for, so that no read looks it up; <see langword="null"/> for a type whose metadata cannot be
    /// resolved, which the reader then reads with the options' own, failing as it always did for that call only.
    /// </summary>
    private static string TypeInfoArgument(ITypeSymbol type)
    {
        return $"{PlanResolveName}<{type.ToDisplayString(OperationClientShape.FullyQualifiedWithNullable)}>(client)";
    }

    private static bool IsRuntimeExecType(INamedTypeSymbol type, string name)
    {
        return type.Name == name && type.ContainingType is null && type.IsInNamespace("Blueprint", "Runtime", "Exec");
    }

    private static bool IsDeclaredPolymorphic(INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(static a => a.AttributeClass is { Name: "JsonPolymorphicAttribute" or "JsonDerivedTypeAttribute" } c
                                                    && c.IsInNamespace("System", "Text", "Json", "Serialization"));
    }

    private static bool DerivesFromStatusCodeResult(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Arity == 0 && IsRuntimeExecType(current, "StatusCodeResult"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSingleton(INamedTypeSymbol type)
    {
        return type.GetMembers("Instance").Any(member =>
            (member is IFieldSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public } field
             && SymbolEqualityComparer.Default.Equals(field.Type, type))
            || (member is IPropertySymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public, GetMethod: not null } property
                && SymbolEqualityComparer.Default.Equals(property.Type, type)));
    }

    private static string Generate(TypedClientModel client)
    {
        var sb = new StringBuilder(1024 + (client.Operations.Count * 1536));
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

        // Including an obsolete operation is the hand-written class's decision, warned of at its include; the calls generated
        // for it would otherwise raise the same warning again where no one can suppress it.
        sb.AppendLine("#pragma warning disable CS0612, CS0618");
        sb.AppendLine();

        var indent = string.Empty;

        if (client.Namespace is not null)
        {
            sb.AppendLine($"namespace {client.Namespace}");
            sb.AppendLine("{");
            indent = "    ";
        }

        GenerateInterface(sb, client, indent);
        sb.AppendLine();
        GenerateClass(sb, client, indent);

        if (client.Namespace is not null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void GenerateInterface(StringBuilder sb, TypedClientModel client, string indent)
    {
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// The calls <see cref=\"{client.ClassName}\"/> makes to a remote Blueprint API, one per operation it includes.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}{(client.IsPublic ? "public" : "internal")} interface {client.InterfaceName}");
        sb.AppendLine($"{indent}{{");

        for (var i = 0; i < client.Operations.Count; i++)
        {
            var operation = client.Operations[i];

            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Sends a <see cref=\"{ToCref(operation.OperationFqn)}\"/> ({operation.HttpMethod}) to the remote API.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    /// <param name=\"operation\">The operation to send.</param>");
            sb.AppendLine($"{indent}    /// <param name=\"options\">Optional per-call request customisation.</param>");
            sb.AppendLine($"{indent}    /// <param name=\"cancellationToken\">Cancellation token.</param>");
            sb.AppendLine($"{indent}    /// <returns>The result the operation declares, as the remote API returned it.</returns>");
            sb.AppendLine($"{indent}    {MethodSignature(operation)};");
        }

        foreach (var method in client.Methods)
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Calls <c>{method.Name}</c>, which <see cref=\"{client.ClassName}\"/> declares.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    {method.Signature};");
        }

        sb.AppendLine();
        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Makes a client that authenticates every request with a credential, applied after every other step that shapes it. It");
        sb.AppendLine($"{indent}    /// replaces any credential this client has; this client is unchanged.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine($"{indent}    /// <param name=\"credential\">The credential to authenticate with.</param>");
        sb.AppendLine($"{indent}    /// <returns>The new client.</returns>");
        sb.AppendLine($"{indent}    {client.InterfaceName} WithCredential(global::Blueprint.HttpClient.IUpstreamCredential credential);");
        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateClass(StringBuilder sb, TypedClientModel client, string indent)
    {
        // No accessibility modifier: the hand-written half declares it, and a generated one could only conflict with it.
        sb.AppendLine($"{indent}partial class {client.ClassName} : {client.InterfaceName}, global::Blueprint.HttpClient.IBlueprintClient<{client.ClassName}>");
        sb.AppendLine($"{indent}{{");

        GenerateState(sb, client, indent + "    ");

        foreach (var operation in client.Operations)
        {
            sb.AppendLine();
            GenerateMethod(sb, operation, indent + "    ");
        }

        sb.AppendLine();
        GenerateClones(sb, indent + "    ", client);
        sb.AppendLine();
        GeneratePlan(sb, client, indent + "    ");

        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateState(StringBuilder sb, TypedClientModel client, string indent)
    {
        const string RequestAction = "global::System.Action<global::System.Net.Http.HttpRequestMessage>";

        sb.AppendLine($"{indent}private readonly global::Blueprint.HttpClient.BlueprintHttpClient {ClientFieldName};");
        sb.AppendLine($"{indent}private readonly {PlanClassName} {PlanFieldName};");
        sb.AppendLine($"{indent}private readonly global::Blueprint.HttpClient.IUpstreamCredential? {CredentialFieldName};");
        sb.AppendLine($"{indent}private readonly {RequestAction}? {RequestModifierFieldName};");
        sb.AppendLine();
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Initialises a new instance of the <see cref=\"{client.ClassName}\"/> class.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}/// <param name=\"client\">The transport every call is sent with, whose base address the operations' URLs are relative to.</param>");

        foreach (var field in client.Fields)
        {
            sb.AppendLine($"{indent}/// <param name=\"{field.ParameterName.TrimStart('@')}\">The value of <c>{field.FieldName}</c>.</param>");
        }

        var fieldParameters = string.Concat(client.Fields.Select(static f => $", {f.TypeFqn} {f.ParameterName}"));

        sb.AppendLine($"{indent}public {client.ClassName}(global::Blueprint.HttpClient.BlueprintHttpClient client{fieldParameters})");
        sb.AppendLine($"{indent}{{");

        // With a context, the transport reads and writes with its metadata ahead of its options' own, whose converters still
        // apply; the derived options are cached per options and context, so the plan for them is found as any other's.
        sb.AppendLine(client.SerializerContextFqn is null
            ? $"{indent}    this.{ClientFieldName} = client ?? throw new global::System.ArgumentNullException(nameof(client));"
            : $"{indent}    this.{ClientFieldName} = (client ?? throw new global::System.ArgumentNullException(nameof(client))).WithSerializerContext({client.SerializerContextFqn}.Default);");
        sb.AppendLine($"{indent}    this.{PlanFieldName} = {PlanClassName}.For(this.{ClientFieldName});");

        foreach (var field in client.Fields)
        {
            sb.AppendLine(field.RefusesNull
                ? $"{indent}    this.{field.FieldName} = {field.ParameterName} ?? throw new global::System.ArgumentNullException(nameof({field.ParameterName}));"
                : $"{indent}    this.{field.FieldName} = {field.ParameterName};");
        }

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        // The registration's way of making a client: each field is resolved from the container, one declared nullable as an
        // optional service, so no reflection finds the constructor or its parameters. Explicit, so a class named Create is not
        // refused (a method cannot be named as its enclosing class) and the class's own surface does not gain the member.
        sb.AppendLine($"{indent}/// <inheritdoc />");
        sb.AppendLine($"{indent}static {client.ClassName} global::Blueprint.HttpClient.IBlueprintClient<{client.ClassName}>.{CreateMethodName}(global::System.IServiceProvider services, global::Blueprint.HttpClient.BlueprintHttpClient client)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    global::System.ArgumentNullException.ThrowIfNull(services);");
        sb.AppendLine();

        var resolved = string.Concat(client.Fields.Select(static f => f.IsNullableValueType
            ? $", ({f.ResolveTypeFqn}?)services.GetService(typeof({f.ResolveTypeFqn}))"
            : f.IsOptional
            ? $", global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{f.ResolveTypeFqn}>(services)"
            : $", global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{f.ResolveTypeFqn}>(services)"));

        sb.AppendLine($"{indent}    return new {client.ClassName}(client{resolved});");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        // A clone shares the client, the plan and the fields, and differs only in what was added to it.
        sb.AppendLine($"{indent}private {client.ClassName}({client.ClassName} source, global::Blueprint.HttpClient.IUpstreamCredential? credential, {RequestAction}? requestModifier)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    this.{ClientFieldName} = source.{ClientFieldName};");
        sb.AppendLine($"{indent}    this.{PlanFieldName} = source.{PlanFieldName};");

        foreach (var field in client.Fields)
        {
            sb.AppendLine($"{indent}    this.{field.FieldName} = source.{field.FieldName};");
        }

        sb.AppendLine($"{indent}    this.{CredentialFieldName} = credential;");
        sb.AppendLine($"{indent}    this.{RequestModifierFieldName} = requestModifier;");
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// Emits the plan every call of the client is made with: each body's metadata, each result's reader given the metadata of
    /// what it reads, and any HTTP method with no static of its own. All of it is a function of the JSON options alone, so it
    /// is resolved once per options instance and shared by every client over them, clones included; making a client is one
    /// lookup, and a call looks nothing up.
    /// </summary>
    /// <remarks>
    /// The plan holds no transport, so the table that keeps it for as long as its options live keeps no <c>HttpClient</c>
    /// alive with it.
    /// </remarks>
    private static void GeneratePlan(StringBuilder sb, TypedClientModel client, string indent)
    {
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// What every call of <see cref=\"{client.ClassName}\"/> sends and reads with for one set of JSON options, resolved once per options");
        sb.AppendLine($"{indent}/// and shared by every client over them.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}private sealed class {PlanClassName}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, {PlanClassName}> _plans = new();");

        foreach (var operation in client.Operations.Where(static o => HttpMethodExpression(o.HttpMethod) is null))
        {
            sb.AppendLine();
            sb.AppendLine($"{indent}    internal static readonly global::System.Net.Http.HttpMethod {PlanMethodName(operation)} = new global::System.Net.Http.HttpMethod({SymbolDisplay.FormatLiteral(operation.HttpMethod, quote: true)});");
        }

        foreach (var operation in client.Operations)
        {
            sb.AppendLine();

            // A GET sends no body, so has no metadata to write one with.
            if (!operation.AppendsQueryString)
            {
                sb.AppendLine($"{indent}    internal readonly global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{operation.OperationFqn}>? {PlanRequestName(operation)};");
            }

            sb.AppendLine($"{indent}    internal readonly global::Blueprint.HttpClient.Readers.ResponseReader<{operation.ResultTypeFqn}> {PlanReaderName(operation)};");
        }

        sb.AppendLine();
        sb.AppendLine($"{indent}    private {PlanClassName}(global::Blueprint.HttpClient.BlueprintHttpClient client)");
        sb.AppendLine($"{indent}    {{");

        foreach (var operation in client.Operations)
        {
            if (!operation.AppendsQueryString)
            {
                sb.AppendLine($"{indent}        this.{PlanRequestName(operation)} = {PlanResolveName}<{operation.OperationFqn}>(client);");
            }

            sb.AppendLine($"{indent}        this.{PlanReaderName(operation)} = {operation.ReaderExpression};");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        // One type that cannot be configured must not stop a client being made: its call fails, as it always did, when the
        // options' own metadata is asked for at the call.
        sb.AppendLine($"{indent}    private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? {PlanResolveName}<T>(global::Blueprint.HttpClient.BlueprintHttpClient client)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        try");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            return client.ResolveTypeInfo<T>();");
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}        catch (global::System.Exception e) when (e is global::System.NotSupportedException or global::System.InvalidOperationException)");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            return null;");
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();
        sb.AppendLine($"{indent}    internal static {PlanClassName} For(global::Blueprint.HttpClient.BlueprintHttpClient client)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        return _plans.TryGetValue(client.JsonOptions, out var plan) ? plan : Create(client);");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        // Apart from For, so that only a miss allocates the callback, which holds the transport only while the plan is made.
        sb.AppendLine($"{indent}    private static {PlanClassName} Create(global::Blueprint.HttpClient.BlueprintHttpClient client)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        return _plans.GetValue(client.JsonOptions, _ => new {PlanClassName}(client));");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateClones(StringBuilder sb, string indent, TypedClientModel client)
    {
        sb.AppendLine($"{indent}/// <inheritdoc />");
        sb.AppendLine($"{indent}public {client.InterfaceName} {WithCredentialMethodName}(global::Blueprint.HttpClient.IUpstreamCredential credential)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    global::System.ArgumentNullException.ThrowIfNull(credential);");
        sb.AppendLine();
        sb.AppendLine($"{indent}    return new {client.ClassName}(this, credential, this.{RequestModifierFieldName});");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Makes a client that also runs a modifier on every request, after the call's own modifier and any this client already has,");
        sb.AppendLine($"{indent}/// and before the credential. This client is unchanged.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}/// <param name=\"modify\">The modifier to run.</param>");
        sb.AppendLine($"{indent}/// <returns>The new client.</returns>");
        sb.AppendLine($"{indent}{(client.IsSealed ? "private" : "protected")} {client.InterfaceName} {WithRequestModifierMethodName}(global::System.Action<global::System.Net.Http.HttpRequestMessage> modify)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    global::System.ArgumentNullException.ThrowIfNull(modify);");
        sb.AppendLine();
        sb.AppendLine($"{indent}    return new {client.ClassName}(this, this.{CredentialFieldName}, this.{RequestModifierFieldName} + modify);");
        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateMethod(StringBuilder sb, TypedClientOperationModel operation, string indent)
    {
        var builders = operation.OperationFqn + "." + HttpClassName;

        // Implemented by the hand-written half where a call needs its options shaped by its operation; a call of one that is
        // not implemented is removed by the compiler.
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// Runs before each <see cref=\"{ToCref(operation.OperationFqn)}\"/> is sent, and may replace the options it is sent with.");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}/// <param name=\"operation\">The operation being sent.</param>");
        sb.AppendLine($"{indent}/// <param name=\"options\">The call's options, which the hook may replace; its modifier runs first of those that shape the request.</param>");

        // A hook that reads no instance state cannot be static, as it implements this; the parts' attributes merge, so this
        // exempts the hand-written one from CA1822.
        sb.AppendLine($"{indent}[global::System.Diagnostics.CodeAnalysis.SuppressMessage(\"Performance\", \"CA1822:Mark members as static\", Justification = \"Implements an instance partial method.\")]");
        sb.AppendLine($"{indent}partial void OnConfigure({operation.OperationFqn} operation, ref global::Blueprint.HttpClient.CallOptions? options);");
        sb.AppendLine();
        sb.AppendLine($"{indent}/// <inheritdoc />");
        sb.AppendLine($"{indent}public {MethodSignature(operation)}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    global::System.ArgumentNullException.ThrowIfNull(operation);");
        sb.AppendLine();
        sb.AppendLine($"{indent}    this.OnConfigure(operation, ref options);");
        sb.AppendLine();
        sb.AppendLine($"{indent}    var url = {builders}.BuildUrl(operation);");
        sb.AppendLine();
        sb.AppendLine($"{indent}    return this.{ClientFieldName}.SendAsync<{operation.OperationFqn}, {operation.ResultTypeFqn}>(");
        sb.AppendLine($"{indent}        {HttpMethodExpression(operation.HttpMethod) ?? (PlanClassName + "." + PlanMethodName(operation))},");

        // A null URL (a route placeholder with no value) is passed on as null, which the transport refuses before sending.
        sb.AppendLine(operation.AppendsQueryString
            ? $"{indent}        url is null ? null : {builders}.AppendQueryString(operation, url),"
            : $"{indent}        url,");
        sb.AppendLine($"{indent}        operation,");
        sb.AppendLine(operation.AppendsQueryString
            ? $"{indent}        null,"
            : $"{indent}        this.{PlanFieldName}.{PlanRequestName(operation)},");
        sb.AppendLine($"{indent}        this.{PlanFieldName}.{PlanReaderName(operation)},");
        sb.AppendLine($"{indent}        {SymbolDisplay.FormatLiteral(operation.OperationName, quote: true)},");

        // The caller's options reach the transport as given, which runs their modifier, then the client's, then the credential.
        sb.AppendLine($"{indent}        options,");
        sb.AppendLine($"{indent}        this.{RequestModifierFieldName},");
        sb.AppendLine($"{indent}        this.{CredentialFieldName},");
        sb.AppendLine($"{indent}        cancellationToken);");
        sb.AppendLine($"{indent}}}");
    }

    private static string MethodSignature(TypedClientOperationModel operation)
    {
        return $"global::System.Threading.Tasks.Task<{operation.ResultTypeFqn}> {operation.MethodName}({operation.OperationFqn} operation, global::Blueprint.HttpClient.CallOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static string? HttpMethodExpression(string httpMethod)
    {
        return httpMethod switch
        {
            "GET" => "global::System.Net.Http.HttpMethod.Get",
            "POST" => "global::System.Net.Http.HttpMethod.Post",
            "PUT" => "global::System.Net.Http.HttpMethod.Put",
            "PATCH" => "global::System.Net.Http.HttpMethod.Patch",
            "DELETE" => "global::System.Net.Http.HttpMethod.Delete",
            "HEAD" => "global::System.Net.Http.HttpMethod.Head",
            "OPTIONS" => "global::System.Net.Http.HttpMethod.Options",
            "TRACE" => "global::System.Net.Http.HttpMethod.Trace",
            _ => null,
        };
    }

    // The plan's members are named for the operation's type, which is unique among a client's includes (BPCL005), and live in
    // the plan, so they add no name to the hand-written class.
    private static string PlanReaderName(TypedClientOperationModel operation)
    {
        return operation.OperationName + "Reader";
    }

    private static string PlanRequestName(TypedClientOperationModel operation)
    {
        return operation.OperationName + "Request";
    }

    private static string PlanMethodName(TypedClientOperationModel operation)
    {
        return operation.OperationName + "Method";
    }

    private static string ToCref(string fullyQualifiedName)
    {
        return fullyQualifiedName.Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The serializer context a client names, as extraction needs it; never part of the model, which keeps only its name.
    /// </summary>
    /// <param name="Fqn">The context's fully qualified name.</param>
    /// <param name="Name">The context's name as a diagnostic displays it.</param>
    /// <param name="CoveredTypes">The fully qualified names of the types its metadata covers (<see cref="CollectCoveredTypes"/>).</param>
    private sealed record SerializerContextInfo(string Fqn, string Name, ImmutableHashSet<string> CoveredTypes);

    private static class Stages
    {
        /// <summary>
        /// Tracks the extraction of each <c>[BlueprintClient]</c> class and the calls it makes.
        /// </summary>
        public const string ClientExtraction = nameof(ClientExtraction);
    }
}
