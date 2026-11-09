using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// The forwarding handler of an operation marked <c>[Expose&lt;TInner&gt;]</c> (D12): found by
/// <c>HandlerDiscovery.FindHandler</c> after the inline and external lookups, and rendered by
/// <see cref="HandlerExecutionModule"/> in place of a handler call.
/// </summary>
/// <remarks>
/// The generated body creates a fresh <c>TInner</c> (D4), copies the operation's properties onto it by name, read from the
/// shared property model (D25), and sends it with <c>BlueprintHttpClient.ForwardAsync</c> (D5) over the named client and
/// JSON options that <c>AddBlueprintClient</c> registered for the <c>[BlueprintClient]</c> that includes <c>TInner</c> (D2,
/// D29). D11's bindings are set in the same initialiser, and the operation's <c>OnMap</c> or <c>OnMapAsync</c> runs on the
/// result before it is sent. Credentials (D7) and error translations (D8) are later issues.
/// </remarks>
public static class GatewayForward
{
    private const string BlueprintClientAttributeMetadataName = "Blueprint.HttpClient.BlueprintClientAttribute";
    private const string BlueprintClientIncludeAttributeMetadataName = "Blueprint.HttpClient.BlueprintClientIncludeAttribute";
    private const string BlueprintClientInterfaceMetadataName = "Blueprint.HttpClient.IBlueprintClient`1";

    /// <summary>
    /// The transport factory the generated forward calls, with the client type as its type argument.
    /// </summary>
    private const string TransportFactory = "global::Blueprint.HttpClient.BlueprintClientTransport.Create";

    /// <summary>
    /// Finds which <c>[BlueprintClient]</c> classes include each operation: the ones declared in the compilation, by their
    /// attributes, and generated ones in referenced assemblies, whose attributes are not compiled in, by the method their
    /// generated half declares for each include.
    /// </summary>
    /// <param name="compilation">The compilation to search.</param>
    /// <returns>The client types, <c>global::</c>-qualified, by the metadata name of each included operation.</returns>
    public static IReadOnlyDictionary<string, List<string>> BuildClientIndex(Compilation compilation)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var clientAttribute = compilation.GetTypeByMetadataName(BlueprintClientAttributeMetadataName);
        var includeAttribute = compilation.GetTypeByMetadataName(BlueprintClientIncludeAttributeMetadataName);
        var clientInterface = compilation.GetTypeByMetadataName(BlueprintClientInterfaceMetadataName);
        if (clientAttribute is null || clientInterface is null)
        {
            return index;
        }

        foreach (var type in GetTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, clientAttribute)))
            {
                continue;
            }

            foreach (var include in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(include.AttributeClass, includeAttribute)
                    && include.ConstructorArguments is [{ Value: INamedTypeSymbol included }])
                {
                    Add(index, included, type);
                }
            }
        }

        var httpClientAssembly = clientInterface.ContainingAssembly;
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assembly.Modules.Any(m => m.ReferencedAssemblySymbols.Contains(httpClientAssembly, SymbolEqualityComparer.Default)))
            {
                continue;
            }

            foreach (var type in GetTypes(assembly.GlobalNamespace))
            {
                if (type.DeclaredAccessibility != Accessibility.Public
                    || !type.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, clientInterface)))
                {
                    continue;
                }

                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, Parameters: [{ Type: INamedTypeSymbol included }, ..] }
                        && method.Name.EndsWith("Async", StringComparison.Ordinal)
                        && included.ImplementsInterface("Blueprint.Model.IOperation"))
                    {
                        Add(index, included, type);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// Creates the forwarding handler of an operation marked <c>[Expose&lt;TInner&gt;]</c>.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <param name="exposedType">The inner operation it exposes.</param>
    /// <param name="clientIndex">The clients that include each operation, from <see cref="BuildClientIndex"/>.</param>
    /// <returns>The handler, or the diagnostic that prevents one.</returns>
    public static Result<OperationHandlerModel> CreateHandler(
        INamedTypeSymbol operationType,
        INamedTypeSymbol exposedType,
        IReadOnlyDictionary<string, List<string>> clientIndex)
    {
        var location = LocationInfo.CreateFrom(operationType.Locations.First());
        var innerTypeName = exposedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (!clientIndex.TryGetValue(exposedType.ToFullMetadataName(), out var clients) || clients.Count == 0)
        {
            return Result<OperationHandlerModel>.Failure(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.NoClientIncludesInnerOperation,
                location,
                [operationType.Name, exposedType.Name]));
        }

        if (clients.Count > 1)
        {
            return Result<OperationHandlerModel>.Failure(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.SeveralClientsIncludeInnerOperation,
                location,
                [operationType.Name, exposedType.Name, string.Join(", ", clients.Select(static c => c.Replace("global::", string.Empty)))]));
        }

        var resultType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (resultType is null)
        {
            // An operation interface with no result: D9 requires the inner operation's own, which carries one.
            return Result<OperationHandlerModel>.Failure(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.MissingOperationInterface,
                location,
                [operationType.Name, exposedType.Name, "IQuery<" + (OperationResultContractHelper.GetDeclaredOperationResultType(exposedType)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "R") + ">"]));
        }

        var resultTypeName = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // D11: the bindings come from the property model's builder, which is what left them out of the public shape. The
        // map hook's parameters after the inner operation are the handler's, so they are resolved as a handler's are.
        var bindings = OperationPropertyModelBuilder.GetBindings(operationType);
        var mapHook = GatewayMapHook.Find(operationType, exposedType, diagnostics: null);
        var mapHookParameters = mapHook is null
            ? EquatableList<HandlerParameter>.Empty
            : Generators.Pipelines.ParameterAnalyzer.AnalyzeParameters(mapHook, operationType, isExternalHandler: true);

        return Result.Success(new OperationHandlerModel(
            OperationTypeName: operationType.Name,
            OperationTypeFullName: operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            OperationNamespace: operationType.ContainingNamespace.ToDisplayString(),
            HandlerType: HandlerType.Forward,
            HandlerMethodName: "ForwardAsync",
            IsHandlerStatic: false,
            IsHandlerAsync: true,
            HandlerReturnTypeName: "global::System.Threading.Tasks.Task<" + resultTypeName + ">",
            HandlerClassName: null,
            HandlerClassFullName: null,
            HandlerClassNamespace: null,
            Parameters: mapHookParameters,
            Forward: new GatewayForwardModel(
                innerTypeName,
                clients[0],
                resultTypeName,
                GetCopiedProperties(operationType, exposedType, bindings),
                bindings,
                mapHook?.Name,
                mapHook?.Name == GatewayMapHook.AsyncName)));
    }

    /// <summary>
    /// Creates the diagnostic for a hand-written handler found on an operation whose handler is generated.
    /// </summary>
    /// <param name="operationType">The operation.</param>
    /// <param name="exposedType">The inner operation it exposes.</param>
    /// <param name="handler">The hand-written handler that was found.</param>
    /// <returns>BPGW005.</returns>
    public static DiagnosticInfo HandWrittenHandler(INamedTypeSymbol operationType, INamedTypeSymbol exposedType, OperationHandlerModel handler)
    {
        var handlerName = handler.HandlerClassName is null
            ? operationType.Name + "." + handler.HandlerMethodName
            : handler.HandlerClassName + "." + handler.HandlerMethodName;

        return new DiagnosticInfo(
            GatewayDiagnosticDescriptors.HandWrittenHandlerOnForwardedOperation,
            LocationInfo.CreateFrom(operationType.Locations.First()),
            [operationType.Name, handlerName, exposedType.Name]);
    }

    /// <summary>
    /// Renders the forward and returns the expression awaiting its result: the inner operation is created and filled in
    /// locals before it.
    /// </summary>
    /// <param name="context">The render context of the pipeline's execution stage.</param>
    /// <param name="forward">The forward to render.</param>
    /// <param name="mapHookArguments">The map hook's arguments after the inner operation, resolved as a handler's are.</param>
    /// <returns>The invocation expression, of type <c>Task&lt;TResult&gt;</c>.</returns>
    public static string RenderInvocation(PipelineRenderContext context, GatewayForwardModel forward, IReadOnlyList<string> mapHookArguments)
    {
        var operation = context.GetOperationExpression();
        var assignments = forward.CopiedPropertyNames.Select(name => name + " = " + operation + "." + name).ToList();

        // D11: bound properties, set in the initialiser so that a required or init-only one is set too. A claim is read
        // into a local first, and a caller without it (or with one that does not convert) is refused before any send.
        foreach (var binding in forward.Bindings)
        {
            switch (binding.Source)
            {
                case GatewayBindingSource.Claim:
                    assignments.Add(binding.PropertyName + " = " + RenderClaim(context, binding));
                    break;
                case GatewayBindingSource.Route:
                case GatewayBindingSource.Property:
                    assignments.Add(binding.PropertyName + " = " + operation + "." + binding.Argument);
                    break;
                case GatewayBindingSource.Value:
                    assignments.Add(binding.PropertyName + " = " + binding.Argument);
                    break;
            }
        }

        // D4: a fresh instance of the exact inner type, so nothing of the gateway operation travels upstream.
        context.DeclareVariable("forwardInner", "new " + forward.InnerTypeName + " { " + string.Join(", ", assignments) + " }");

        if (forward.MapHookName is not null)
        {
            var call = operation + "." + forward.MapHookName + "(" + string.Join(", ", new[] { "forwardInner" }.Concat(mapHookArguments)) + ")";
            context.WriteStatement(forward.IsMapHookAsync ? "await " + call + ".ConfigureAwait(false);" : call + ";");
        }

        context.DeclareVariable(
            "forwardTransport",
            TransportFactory + "<" + forward.ClientTypeName + ">(" + context.GetContextPropertyExpression("ServiceProvider") + ")");
        context.WriteBlankLine();

        return "forwardTransport.ForwardAsync<" + forward.InnerTypeName + ", " + forward.ResultTypeName + ">(forwardInner, null, "
            + context.GetContextPropertyExpression("OperationCancelled") + ")";
    }

    private static EquatableList<string> GetCopiedProperties(
        INamedTypeSymbol operationType,
        INamedTypeSymbol exposedType,
        EquatableList<GatewayPropertyBinding> bindings)
    {
        var innerSettable = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (var member in exposedType.GetAllMembers().OfType<IPropertySymbol>())
        {
            // A bound property takes its binding's value, never a caller's, even where the operation declares one of its name.
            if (member is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, SetMethod.DeclaredAccessibility: Accessibility.Public }
                && !innerSettable.ContainsKey(member.Name)
                && !bindings.Any(b => b.PropertyName == member.Name))
            {
                innerSettable.Add(member.Name, member);
            }
        }

        var copied = new List<string>();
        foreach (var property in OperationPropertyModelBuilder.GetProperties(operationType))
        {
            if (!innerSettable.TryGetValue(property.Info.Name, out var innerProperty))
            {
                continue;
            }

            // A contributed property is the inner one. A declared one is copied only when it has the inner property's
            // type: a declared property of another type is the gateway's own (D10's declared mode maps those, #68).
            if (property.Info.Origin == OperationPropertyOrigin.Contributed
                || SymbolEqualityComparer.IncludeNullability.Equals(property.Member.Type, innerProperty.Type)
                || SymbolEqualityComparer.Default.Equals(property.Member.Type, innerProperty.Type))
            {
                copied.Add(property.Info.Name);
            }
        }

        return EquatableList<string>.TakeOwnership(copied);
    }

    /// <summary>
    /// Reads a bound claim into a local of the inner property's type, converted as a route value is, and returns the local.
    /// </summary>
    private static string RenderClaim(PipelineRenderContext context, GatewayPropertyBinding binding)
    {
        var claimType = binding.Argument!.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var claimVariable = "_claim_" + binding.PropertyName;
        var boundVariable = "_bound_" + binding.PropertyName;
        var forbidden = "new global::Blueprint.ForbiddenException(\"The caller's '" + claimType + "' claim, which this operation needs, ";

        var read = context.GetContextPropertyExpression("UserPrincipal") + "?.FindFirst(\"" + claimType + "\")?.Value ?? throw "
            + forbidden + "is missing.\")";

        switch (binding.ClaimConversion)
        {
            case GatewayClaimConversion.String:
                context.DeclareVariable(boundVariable, read);
                break;

            case GatewayClaimConversion.TryParse:
                context.DeclareVariable(claimVariable, read);
                context.If(
                    "!" + binding.TryParseFormat!.Replace(GatewayBindings.ValueToken, claimVariable).Replace(GatewayBindings.ParsedToken, boundVariable),
                    then => then.Throw(forbidden + "is not a valid " + binding.TargetTypeName.Replace("global::", string.Empty) + ".\")"));
                break;

            default:
                context.DeclareVariable(claimVariable, read);
                context.WriteStatement(binding.TargetTypeName + " " + boundVariable + ";");
                context.Try(@try => @try.WriteStatement(
                    boundVariable + " = (" + binding.TargetTypeName + ")global::System.ComponentModel.TypeDescriptor.GetConverter(typeof("
                    + binding.TargetTypeName + ")).ConvertFromInvariantString(" + claimVariable + ")!;"));
                context.Catch("global::System.Exception", @catch => @catch.Throw(
                    forbidden + "is not a valid " + binding.TargetTypeName.Replace("global::", string.Empty) + ".\")"));
                break;
        }

        return boundVariable;
    }

    private static void Add(Dictionary<string, List<string>> index, INamedTypeSymbol included, INamedTypeSymbol client)
    {
        var key = included.ToFullMetadataName();
        if (!index.TryGetValue(key, out var clients))
        {
            clients = [];
            index.Add(key, clients);
        }

        var clientName = client.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!clients.Contains(clientName))
        {
            clients.Add(clientName);
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamedTypeSymbol type)
            {
                yield return type;
            }
            else if (member is INamespaceSymbol child)
            {
                foreach (var nested in GetTypes(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
