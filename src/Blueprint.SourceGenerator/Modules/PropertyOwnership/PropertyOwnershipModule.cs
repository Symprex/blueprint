using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Determines compile-time property ownership (route, body, query, header, cookie, system) and generates
/// the Population pipeline stage that binds HTTP request data onto operation properties.
/// </summary>
public sealed class PropertyOwnershipModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9]+)\}", RegexOptions.Compiled);

    /// <inheritdoc />
    public string ModuleId => nameof(PropertyOwnershipModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PropertyOwnershipModule);

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        if (!HasHttpHostExposure(context))
        {
            return;
        }

        var operationType = context.GetOperationTypeSymbol();
        var routePlaceholders = ExtractRoutePlaceholders(operationType);
        var httpMethods = ResolveHttpMethods(context, operationType);

        var hasErrors = false;
        var entries = AnalyzePropertyOwnership(context, operationType, routePlaceholders, httpMethods, ref hasErrors);

        if (hasErrors)
        {
            return;
        }

        if (entries.Count == 0)
        {
            return;
        }

        context.AddRenderStep(writer => RenderDescriptorFeatureData(writer, entries));
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        if (!HasHttpHostExposure(operationType))
        {
            return;
        }

        var routePlaceholders = ExtractRoutePlaceholders(operationType);
        var httpMethods = ResolveHttpMethods(operationType);
        var entries = AnalyzePropertyOwnership(operationType, routePlaceholders, httpMethods);

        if (entries.Count == 0)
        {
            return;
        }

        var hasBodyProperties = entries.Any(static e => e.Source == PropertyOwnershipSource.Body);
        if (hasBodyProperties)
        {
            context.RequireAsync();
            context.AddRenderStep(PipelineStage.Population, renderContext => RenderPopulationStage(renderContext, entries, context.Operation));
        }
        else if (entries.Any(static e => e.Source == PropertyOwnershipSource.Route))
        {
            context.AddRenderStep(PipelineStage.Population, renderContext => RenderRouteOnlyPopulation(renderContext, entries, context.Operation));
        }
    }

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        // In the pipeline context we don't have descriptor metadata.
        // Detect HTTP exposure by checking for RootLink or SelfLink attributes.
        return operationType.GetAttributes().Any(static a =>
            a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static List<string> ExtractRoutePlaceholders(INamedTypeSymbol operationType)
    {
        var placeholders = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is not ("RootLinkAttribute" or "SelfLinkAttribute"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string url)
            {
                foreach (Match match in PlaceholderRegex.Matches(url))
                {
                    var name = match.Groups[1].Value;
                    if (!placeholders.Contains(name, StringComparer.Ordinal))
                    {
                        placeholders.Add(name);
                    }
                }
            }
        }

        return placeholders;
    }

    private static string[] ResolveHttpMethods(DescriptorContributorContext context, INamedTypeSymbol operationType)
    {
        var methods = context.GetMetadata<OperationHttpMethodMetadataRegistration>()
            .Select(static r => r.HttpMethod)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (methods.Length > 0)
        {
            return methods;
        }

        // Fallback to convention
        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static string[] ResolveHttpMethods(INamedTypeSymbol operationType)
    {
        // Re-derive from attributes in pipeline context (no descriptor metadata available)
        var explicitMethods = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;

            switch (name)
            {
                case "HttpGetAttribute":
                    explicitMethods.Add("GET");
                    break;
                case "HttpPostAttribute":
                    explicitMethods.Add("POST");
                    break;
                case "HttpPutAttribute":
                    explicitMethods.Add("PUT");
                    break;
                case "HttpPatchAttribute":
                    explicitMethods.Add("PATCH");
                    break;
                case "HttpDeleteAttribute":
                    explicitMethods.Add("DELETE");
                    break;
                case "HttpMethodAttribute" when attribute.ConstructorArguments.Length > 0:
                    if (attribute.ConstructorArguments[0].Value is string method)
                    {
                        explicitMethods.Add(method.ToUpperInvariant());
                    }

                    break;
            }
        }

        if (explicitMethods.Count > 0)
        {
            return explicitMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
            && candidate.Name == "ICommand");
    }

    private static bool IsBodyMethod(string[] httpMethods)
    {
        return httpMethods.Any(static m =>
            string.Equals(m, "POST", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "PUT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m, "PATCH", StringComparison.OrdinalIgnoreCase));
    }

    private static List<PropertyOwnershipEntry> AnalyzePropertyOwnership(
        DescriptorContributorContext context,
        INamedTypeSymbol operationType,
        List<string> routePlaceholders,
        string[] httpMethods,
        ref bool hasErrors)
    {
        var entries = new List<PropertyOwnershipEntry>();

        foreach (var member in operationType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.IsReadOnly || member.SetMethod is null || member.SetMethod.IsInitOnly || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (IsSystemProperty(member))
            {
                continue;
            }

            if (routePlaceholders.Contains(member.Name, StringComparer.Ordinal))
            {
                if (!IsSupportedRouteType(member.Type))
                {
                    var location = member.Locations.FirstOrDefault(static l => l.IsInSource);
                    context.AddDiagnostic(new DiagnosticInfo(
                        PropertyOwnershipDiagnosticDescriptors.UnsupportedRoutePropertyType,
                        location is not null ? LocationInfo.CreateFrom(location) : null,
                        [member.Name, operationType.Name, member.Type.ToDisplayString()]));
                    hasErrors = true;
                    continue;
                }

                entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name));
                continue;
            }

            if (IsBodyMethod(httpMethods))
            {
                entries.Add(new PropertyOwnershipEntry(
                    member.Name,
                    member.Type,
                    PropertyOwnershipSource.Body,
                    GetCamelCaseName(member.Name)));
            }

            // GET/DELETE: remaining unowned properties are left unbound in Slice 1.
            // Query binding comes in Slice 2.
        }

        return entries;
    }

    private static List<PropertyOwnershipEntry> AnalyzePropertyOwnership(
        INamedTypeSymbol operationType,
        List<string> routePlaceholders,
        string[] httpMethods)
    {
        var entries = new List<PropertyOwnershipEntry>();

        foreach (var member in operationType.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.IsReadOnly || member.SetMethod is null || member.SetMethod.IsInitOnly || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (IsSystemProperty(member))
            {
                continue;
            }

            if (routePlaceholders.Contains(member.Name, StringComparer.Ordinal))
            {
                if (!IsSupportedRouteType(member.Type))
                {
                    continue;
                }

                entries.Add(new PropertyOwnershipEntry(member.Name, member.Type, PropertyOwnershipSource.Route, member.Name));
                continue;
            }

            if (IsBodyMethod(httpMethods))
            {
                entries.Add(new PropertyOwnershipEntry(
                    member.Name,
                    member.Type,
                    PropertyOwnershipSource.Body,
                    GetCamelCaseName(member.Name)));
            }
        }

        return entries;
    }

    private static bool IsSystemProperty(IPropertySymbol property)
    {
        var typeName = property.Type.ToDisplayString();
        return typeName is "System.Threading.CancellationToken"
            or "Microsoft.Extensions.Logging.ILogger";
    }

    private static bool IsSupportedRouteType(ITypeSymbol type)
    {
        // Strip nullable annotation for type checking
        var checkType = type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;

        var displayName = checkType.ToDisplayString();
        return displayName is "string"
            or "int"
            or "long"
            or "System.Guid"
            or "System.DateTime"
            or "bool"
            or "decimal"
            || type.TypeKind == TypeKind.Enum;
    }

    private static string GetCamelCaseName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static void RenderDescriptorFeatureData(ICodeWriter writer, List<PropertyOwnershipEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var entryExpressions = entries
            .Select(static e =>
            {
                var sourceExpr = $"global::Blueprint.Model.PropertyBindingSource.{e.Source}";
                var bindingNameExpr = e.BindingName is not null
                    ? CSharpSymbolDisplay.FormatLiteral(e.BindingName, quote: true)
                    : "null";
                var propertyNameExpr = CSharpSymbolDisplay.FormatLiteral(e.PropertyName, quote: true);

                return $"new global::Blueprint.Model.OperationPropertyBindingEntry({propertyNameExpr}, {sourceExpr}, {bindingNameExpr})";
            })
            .ToArray();

        writer.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationPropertyOwnershipFeatureData("
            + $"global::System.Collections.Immutable.ImmutableArray.Create({string.Join(", ", entryExpressions)})));");
    }

    private static void RenderPopulationStage(PipelineRenderContext context, List<PropertyOwnershipEntry> entries, OperationModel operation)
    {
        var bodyEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Body).ToArray();
        var routeEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Route).ToArray();
        var operationExpr = context.GetOperationExpression();
        var operationTypeFull = context.GetOperationTypeFullName();

        context.WriteStatement("var httpContext = ((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext;");
        context.WriteBlankLine();

        // Body binding: buffer stream async, then parse with Utf8JsonReader in a static local function
        if (bodyEntries.Length > 0)
        {
            context.If(
                "httpContext.Request.ContentLength > 0 || httpContext.Request.ContentType != null",
                bodyContext =>
                {
                    bodyContext.If(
                        "httpContext.Request.ContentType != null && !httpContext.Request.ContentType.Contains(\"json\", System.StringComparison.OrdinalIgnoreCase)",
                        unsupportedCtx =>
                        {
                            unsupportedCtx.WriteStatement("httpContext.Response.StatusCode = 415;");
                            unsupportedCtx.Return("new Blueprint.Runtime.Http.HttpPipelineShortCircuitResult()");
                        });

                    bodyContext.WriteBlankLine();

                    // Buffer the body asynchronously, then hand off to the sync Utf8JsonReader helper
                    bodyContext.WriteStatement("using var bodyStream = httpContext.Request.ContentLength.HasValue");
                    bodyContext.WriteStatement("    ? new System.IO.MemoryStream((int)httpContext.Request.ContentLength.Value)");
                    bodyContext.WriteStatement("    : new System.IO.MemoryStream();");
                    bodyContext.WriteStatement("await httpContext.Request.Body.CopyToAsync(bodyStream, context.OperationCancelled);");
                    bodyContext.WriteBlankLine();

                    bodyContext.WriteStatement("if (bodyStream.Length > 0)");
                    bodyContext.WriteStatement("{");
                    bodyContext.WriteStatement("    try");
                    bodyContext.WriteStatement("    {");
                    bodyContext.WriteStatement($"        PopulateFromJsonBody(new System.ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length), {operationExpr});");
                    bodyContext.WriteStatement("    }");
                    bodyContext.WriteStatement("    catch (System.Text.Json.JsonException)");
                    bodyContext.WriteStatement("    {");
                    bodyContext.WriteStatement("        httpContext.Response.StatusCode = 400;");
                    bodyContext.WriteStatement("        return new Blueprint.Runtime.Http.HttpPipelineShortCircuitResult();");
                    bodyContext.WriteStatement("    }");
                    bodyContext.WriteStatement("    catch (System.InvalidOperationException)");
                    bodyContext.WriteStatement("    {");
                    bodyContext.WriteStatement("        httpContext.Response.StatusCode = 400;");
                    bodyContext.WriteStatement("        return new Blueprint.Runtime.Http.HttpPipelineShortCircuitResult();");
                    bodyContext.WriteStatement("    }");
                    bodyContext.WriteStatement("}");
                });

            context.WriteBlankLine();
        }

        // Route binding (after body, so route values take precedence)
        foreach (var entry in routeEntries)
        {
            WriteRouteExtraction(context, operationExpr, entry);
        }

        // Emit the static local function for Utf8JsonReader-based body parsing after all pipeline statements.
        // Static local functions avoid ref-struct-in-async (CS4012) while keeping Utf8JsonReader's zero-alloc perf.
        if (bodyEntries.Length > 0)
        {
            context.WriteBlankLine();
            WritePopulateFromJsonBodyLocalFunction(context, operationExpr, operationTypeFull, bodyEntries);
        }
    }

    private static void RenderRouteOnlyPopulation(PipelineRenderContext context, List<PropertyOwnershipEntry> entries, OperationModel operation)
    {
        var routeEntries = entries.Where(static e => e.Source == PropertyOwnershipSource.Route).ToArray();
        var operationExpr = context.GetOperationExpression();

        context.WriteStatement("var httpContext = ((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext;");
        context.WriteBlankLine();

        foreach (var entry in routeEntries)
        {
            WriteRouteExtraction(context, operationExpr, entry);
        }
    }

    private static void WritePopulateFromJsonBodyLocalFunction(
        PipelineRenderContext context,
        string operationExpr,
        string operationTypeFull,
        PropertyOwnershipEntry[] bodyEntries)
    {
        context.WriteStatement($"static void PopulateFromJsonBody(System.ReadOnlySpan<byte> bodyBytes, {operationTypeFull} {operationExpr})");
        context.WriteStatement("{");
        context.WriteStatement("    var reader = new System.Text.Json.Utf8JsonReader(bodyBytes);");
        context.WriteStatement("    if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject) return;");
        context.WriteBlankLine();
        context.WriteStatement("    while (reader.Read())");
        context.WriteStatement("    {");
        context.WriteStatement("        if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject) break;");
        context.WriteStatement("        if (reader.TokenType != System.Text.Json.JsonTokenType.PropertyName) continue;");
        context.WriteBlankLine();

        var isFirst = true;
        foreach (var entry in bodyEntries)
        {
            var prefix = isFirst ? "        if" : "        else if";
            isFirst = false;

            var utf8Name = $"\"{entry.BindingName}\"u8";
            context.WriteStatement($"{prefix} (reader.ValueTextEquals({utf8Name}))");
            context.WriteStatement("        {");
            context.WriteStatement("            reader.Read();");
            WriteUtf8ReaderAssignment(context, operationExpr, entry);
            context.WriteStatement("        }");
        }

        context.WriteStatement("        else");
        context.WriteStatement("        {");
        context.WriteStatement("            reader.Skip();");
        context.WriteStatement("        }");
        context.WriteStatement("    }");
        context.WriteStatement("}");
    }

    private static void WriteUtf8ReaderAssignment(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var typeDisplay = entry.TypeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        switch (typeDisplay)
        {
            case "string":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetString();");
                break;
            case "int":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetInt32();");
                break;
            case "long":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetInt64();");
                break;
            case "bool":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetBoolean();");
                break;
            case "decimal":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetDecimal();");
                break;
            case "System.Guid":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetGuid();");
                break;
            case "System.DateTime":
                context.WriteStatement($"            {operationExpr}.{entry.PropertyName} = reader.GetDateTime();");
                break;
            default:
                // Delegate to STJ for complex types via ref reader overload
                context.WriteStatement(
                    $"            {operationExpr}.{entry.PropertyName} = System.Text.Json.JsonSerializer.Deserialize<global::{typeDisplay}>(ref reader);");
                break;
        }
    }

    private static void WriteRouteExtraction(PipelineRenderContext context, string operationExpr, PropertyOwnershipEntry entry)
    {
        var typeDisplay = entry.TypeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var routeValueExpr = $"httpContext.Request.RouteValues[\"{entry.BindingName}\"]";

        if (typeDisplay == "string")
        {
            context.WriteStatement($"{operationExpr}.{entry.PropertyName} = (string){routeValueExpr};");
        }
        else
        {
            context.If($"{routeValueExpr} is string routeStr_{entry.PropertyName}", ifCtx =>
            {
                var convertExpr = GetRouteConversionExpression(typeDisplay, $"routeStr_{entry.PropertyName}");
                ifCtx.WriteStatement($"{operationExpr}.{entry.PropertyName} = {convertExpr};");
            });
        }
    }

    private static string GetRouteConversionExpression(string typeDisplayName, string stringVariable)
    {
        return typeDisplayName switch
        {
            "int" => $"int.Parse({stringVariable}, System.Globalization.CultureInfo.InvariantCulture)",
            "long" => $"long.Parse({stringVariable}, System.Globalization.CultureInfo.InvariantCulture)",
            "bool" => $"bool.Parse({stringVariable})",
            "decimal" => $"decimal.Parse({stringVariable}, System.Globalization.CultureInfo.InvariantCulture)",
            "System.Guid" => $"System.Guid.Parse({stringVariable})",
            "System.DateTime" => $"System.DateTime.Parse({stringVariable}, System.Globalization.CultureInfo.InvariantCulture)",
            _ => $"({typeDisplayName}){stringVariable}", // enums or fallback
        };
    }

    /// <summary>
    /// Internal model used during analysis. Not exposed to consumers — the feature data record uses string-based source names.
    /// </summary>
    internal sealed record PropertyOwnershipEntry(
        string PropertyName,
        ITypeSymbol TypeSymbol,
        PropertyOwnershipSource Source,
        string? BindingName);

    /// <summary>
    /// The binding source for a property during the ownership analysis phase.
    /// </summary>
    internal enum PropertyOwnershipSource
    {
        /// <summary>Property is populated from a route placeholder.</summary>
        Route,

        /// <summary>Property is populated from the JSON request body.</summary>
        Body,

        /// <summary>Property is populated from the query string.</summary>
        Query,

        /// <summary>Property is populated from an HTTP header.</summary>
        Header,

        /// <summary>Property is populated from an HTTP cookie.</summary>
        Cookie,

        /// <summary>Property is populated by the auth pipeline.</summary>
        AuthContext,

        /// <summary>Property is populated by the runtime.</summary>
        System,
    }
}
