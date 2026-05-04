using System.Collections.Immutable;
using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.ResourceEvents;

/// <summary>
/// Incremental source generator that processes <c>[ResourceEventFamily]</c>-attributed static partial classes and
/// generates boilerplate for each nested partial record: stable EventId constants, implicit conversion operators to
/// <c>ResourceEventResult&lt;T&gt;</c> or <c>KeyOnlyResourceEventResult</c>, and <c>ToOperationResult()</c> factory methods.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ResourceEventFamilySourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private const string ResourceEventFamilyAttributeMetadataName = "Blueprint.Model.Attributes.ResourceEventFamilyAttribute";
    private const string ResourceEventReasonAttributeMetadataName = "Blueprint.Model.Attributes.ResourceEventReasonAttribute";
    private const string ResourceEventKeyAttributeMetadataName = "Blueprint.Model.Attributes.ResourceEventKeyAttribute";
    private const string ResourceEventBodyAttributeMetadataName = "Blueprint.Model.Attributes.ResourceEventBodyAttribute";

    private static readonly DiagnosticDescriptor _familyNotPartialRule = new(
        id: "BPRE001",
        title: "Resource event family must be partial",
        messageFormat: "The resource event family class '{0}' must be declared as partial",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _recordNotPartialRule = new(
        id: "BPRE002",
        title: "Resource event record must be partial",
        messageFormat: "The resource event record '{0}' in family '{1}' must be declared as partial",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _ambiguousShapeRule = new(
        id: "BPRE003",
        title: "Ambiguous resource event shape",
        messageFormat: "The resource event record '{0}' has an ambiguous constructor shape; use [ResourceEventKey] and/or [ResourceEventBody] to clarify roles",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _duplicateReasonRule = new(
        id: "BPRE004",
        title: "Duplicate event reason within family",
        messageFormat: "The reason '{0}' is declared more than once in resource event family '{1}'",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _missingResourceObjectRule = new(
        id: "BPRE005",
        title: "ResourceObject must be specified",
        messageFormat: "The resource event family '{0}' must declare ResourceObject to provide a stable event ID prefix",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _missingIdPropertyRule = new(
        id: "BPRE006",
        title: "Resource type missing Id property for resourceId derivation",
        messageFormat: "The resource type '{0}' does not have a public 'Id' property; body-bearing events cannot derive resourceId automatically",
        category: "Blueprint.ResourceEvents",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var familyDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ResourceEventFamilyAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TransformFamily(ctx))
            .Where(static m => m is not null)
            .WithTrackingName(Stages.InitialExtraction);

        context.RegisterSourceOutput(
            familyDeclarations,
            static (spc, result) => EmitFamilySource(spc, result!));
    }

    private static FamilyResult? TransformFamily(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol familySymbol)
        {
            return null;
        }

        var classNode = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        // Validate partial
        if (!classNode.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(new DiagnosticInfo(_familyNotPartialRule, classNode.Identifier.GetLocation(), new EquatableList<string>([familySymbol.Name])));
            return new FamilyResult(null, new EquatableList<DiagnosticInfo>(diagnostics.ToArray()));
        }

        // Extract family attribute data
        var familyAttr = ctx.Attributes.FirstOrDefault(static a =>
            a.AttributeClass?.ToDisplayString() == ResourceEventFamilyAttributeMetadataName);
        if (familyAttr is null)
        {
            return null;
        }

        // ResourceType (constructor arg 0)
        var resourceType = familyAttr.ConstructorArguments.Length > 0
            ? familyAttr.ConstructorArguments[0].Value as INamedTypeSymbol
            : null;
        if (resourceType is null)
        {
            return null;
        }

        // ResourceObject (named arg)
        var resourceObject = familyAttr.NamedArguments
            .Where(static kvp => kvp.Key == "ResourceObject")
            .Select(static kvp => kvp.Value.Value as string)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resourceObject))
        {
            diagnostics.Add(new DiagnosticInfo(_missingResourceObjectRule, classNode.Identifier.GetLocation(), new EquatableList<string>([familySymbol.Name])));
            return new FamilyResult(null, new EquatableList<DiagnosticInfo>(diagnostics.ToArray()));
        }

        // KeyType (named arg, default to string)
        var keyType = familyAttr.NamedArguments
            .Where(static kvp => kvp.Key == "KeyType")
            .Select(static kvp => kvp.Value.Value as INamedTypeSymbol)
            .FirstOrDefault();
        var keyTypeFullName = keyType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.String";

        // Check resource type has Id property for body-bearing events
        var resourceIdProperty = FindIdProperty(resourceType);
        var resourceTypeFullName = resourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Find nested event records
        var familyNamespace = familySymbol.ContainingNamespace.ToDisplayString();
        var familyClassName = familySymbol.Name;
        var events = new List<EventRecordInfo>();
        var reasonsSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in familySymbol.GetTypeMembers())
        {
            if (member.TypeKind != TypeKind.Class || !member.IsRecord)
            {
                continue;
            }

            // Check for [ResourceEventReason]
            var reasonAttr = member.GetAttributes()
                .FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == ResourceEventReasonAttributeMetadataName);
            if (reasonAttr is null)
            {
                continue;
            }

            // Check partial
            var recordSyntax = member.DeclaringSyntaxReferences
                .Select(static r => r.GetSyntax())
                .OfType<RecordDeclarationSyntax>()
                .FirstOrDefault();
            if (recordSyntax is not null && !recordSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                diagnostics.Add(new DiagnosticInfo(_recordNotPartialRule, recordSyntax.Identifier.GetLocation(), [member.Name, familyClassName]));
                continue;
            }

            // Extract reason
            var reason = reasonAttr.ConstructorArguments.Length >= 2
                ? reasonAttr.ConstructorArguments[1].Value as string
                : null;
            if (string.IsNullOrWhiteSpace(reason))
            {
                continue;
            }

            // Duplicate reason check
            if (!reasonsSeen.Add(reason!))
            {
                var reasonLocation = recordSyntax?.Identifier.GetLocation();
                diagnostics.Add(new DiagnosticInfo(_duplicateReasonRule, reasonLocation, [reason!, familyClassName]));
                continue;
            }

            // Determine event shape: body-bearing vs key-only
            var eventShape = AnalyzeEventShape(member, resourceType, familyClassName, diagnostics);
            if (eventShape is null)
            {
                continue;
            }

            // Validate Id property exists for body-bearing events that derive resource ID from the body
            var hasExplicitKeyForId = eventShape.Value.KeyParameterName is not null;
            if (eventShape.Value.IsBodyBearing && !hasExplicitKeyForId && resourceIdProperty is null)
            {
                var loc = recordSyntax?.Identifier.GetLocation();
                diagnostics.Add(new DiagnosticInfo(_missingIdPropertyRule, loc, [resourceType.Name]));
                continue;
            }

            var eventId = $"{resourceObject}.{reason}";

            // When an explicit key is present, always use it for the resource ID
            var hasExplicitKey = eventShape.Value.KeyParameterName is not null;
            var resourceIdExpression = eventShape.Value.IsBodyBearing && !hasExplicitKey
                ? $"this.{eventShape.Value.BodyParameterName}.{resourceIdProperty?.Name ?? "Id"}.ToString()"
                : $"this.{eventShape.Value.KeyParameterName}.ToString()";

            // The content type for ResourceEventResult<T> is the body type when explicitly specified,
            // otherwise it falls back to the family resource type.
            var contentTypeFullName = eventShape.Value.BodyTypeFullName ?? resourceTypeFullName;

            events.Add(new EventRecordInfo(
                RecordName: member.Name,
                EventId: eventId,
                IsBodyBearing: eventShape.Value.IsBodyBearing,
                BodyParameterName: eventShape.Value.BodyParameterName,
                KeyParameterName: eventShape.Value.KeyParameterName,
                ResourceTypeFullName: resourceTypeFullName,
                ContentTypeFullName: contentTypeFullName,
                KeyTypeFullName: keyTypeFullName,
                ResourceIdExpression: resourceIdExpression));
        }

        if (events.Count == 0 && diagnostics.Count == 0)
        {
            return null;
        }

        return new FamilyResult(
            events.Count > 0
                ? new FamilyToGenerate(familyNamespace, familyClassName, [.. events])
                : null,
            new EquatableList<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static IPropertySymbol? FindIdProperty(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { Name: "Id", DeclaredAccessibility: Accessibility.Public, IsStatic: false } property)
                {
                    return property;
                }
            }
        }

        return null;
    }

    private static EventShapeResult? AnalyzeEventShape(
        INamedTypeSymbol recordType,
        INamedTypeSymbol familyResourceType,
        string familyClassName,
        List<DiagnosticInfo> diagnostics)
    {
        var primaryCtor = recordType.InstanceConstructors
            .FirstOrDefault(static c => c.Parameters.Length > 0 && !c.IsImplicitlyDeclared);
        if (primaryCtor is null || primaryCtor.Parameters.Length == 0)
        {
            // Key-only with no parameters — not valid, needs at least a key
            var loc = recordType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is RecordDeclarationSyntax rds
                ? rds.Identifier.GetLocation()
                : null;
            diagnostics.Add(new DiagnosticInfo(_ambiguousShapeRule, loc, [recordType.Name]));
            return null;
        }

        // Check for explicit [ResourceEventKey] and [ResourceEventBody] attributes
        string? explicitKeyParam = null;
        string? explicitBodyParam = null;
        string? explicitBodyTypeFullName = null;

        foreach (var param in primaryCtor.Parameters)
        {
            foreach (var attr in param.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ResourceEventKeyAttributeMetadataName)
                {
                    explicitKeyParam = param.Name;
                }
                else if (attr.AttributeClass?.ToDisplayString() == ResourceEventBodyAttributeMetadataName)
                {
                    explicitBodyParam = param.Name;
                    explicitBodyTypeFullName = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        // If explicit attributes are used, trust them
        if (explicitKeyParam is not null)
        {
            return new EventShapeResult(
                IsBodyBearing: explicitBodyParam is not null,
                BodyParameterName: explicitBodyParam,
                KeyParameterName: explicitKeyParam,
                BodyTypeFullName: explicitBodyTypeFullName);
        }

        // Infer from constructor parameters
        if (primaryCtor.Parameters.Length == 1)
        {
            var singleParam = primaryCtor.Parameters[0];
            var paramType = singleParam.Type;

            // If the parameter type matches the family resource type, it's body-bearing
            if (SymbolEqualityComparer.Default.Equals(paramType, familyResourceType))
            {
                return new EventShapeResult(
                    IsBodyBearing: true,
                    BodyParameterName: singleParam.Name,
                    KeyParameterName: null,
                    BodyTypeFullName: null);
            }

            // Otherwise, treat as key-only (the single param is the key)
            return new EventShapeResult(
                IsBodyBearing: false,
                BodyParameterName: null,
                KeyParameterName: singleParam.Name,
                BodyTypeFullName: null);
        }

        // Multiple parameters without explicit attributes — ambiguous
        var location = recordType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is RecordDeclarationSyntax rds2
            ? rds2.Identifier.GetLocation()
            : null;
        diagnostics.Add(new DiagnosticInfo(_ambiguousShapeRule, location, [recordType.Name]));
        return null;
    }

    private static void EmitFamilySource(SourceProductionContext spc, FamilyResult result)
    {
        foreach (var diag in result.Diagnostics)
        {
            spc.ReportDiagnostic(diag.ToDiagnostic());
        }

        if (result.Family is not { } family)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {family.Namespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    partial class {family.ClassName}");
        sb.AppendLine("    {");

        foreach (var evt in family.Events)
        {
            sb.AppendLine($"        partial record {evt.RecordName}");
            sb.AppendLine("        {");
            sb.AppendLine($"            /// <summary>");
            sb.AppendLine($"            /// The stable event identifier for this resource event outcome.");
            sb.AppendLine($"            /// </summary>");
            sb.AppendLine($"            public const string EventId = \"{evt.EventId}\";");
            sb.AppendLine();

            if (evt.IsBodyBearing)
            {
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Converts this event record to the canonical <see cref=\"global::Blueprint.Runtime.Exec.ResourceEventResult{{T}}\"/> for pipeline processing.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            /// <returns>A <see cref=\"global::Blueprint.Runtime.Exec.ResourceEventResult{{T}}\"/> that wraps this event's payload.</returns>");
                sb.AppendLine($"            public global::Blueprint.Runtime.Exec.ResourceEventResult<{evt.ContentTypeFullName}> ToOperationResult()");
                sb.AppendLine("            {");
                sb.AppendLine($"                return new global::Blueprint.Runtime.Exec.ResourceEventResult<{evt.ContentTypeFullName}>(EventId, {evt.ResourceIdExpression}, this.{evt.BodyParameterName});");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Enables implicit conversion to <see cref=\"global::Blueprint.Runtime.Exec.ResourceEventResult{{T}}\"/> so handlers can return event records directly.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            public static implicit operator global::Blueprint.Runtime.Exec.ResourceEventResult<{evt.ContentTypeFullName}>({evt.RecordName} value)");
                sb.AppendLine("            {");
                sb.AppendLine("                return value.ToOperationResult();");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Enables implicit conversion to <see cref=\"global::Blueprint.OperationResult\"/> for pipeline result normalization.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            public static implicit operator global::Blueprint.OperationResult({evt.RecordName} value)");
                sb.AppendLine("            {");
                sb.AppendLine("                return value.ToOperationResult();");
                sb.AppendLine("            }");
            }
            else
            {
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Converts this key-only event record to the canonical <see cref=\"global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult\"/> for pipeline processing.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            /// <returns>A <see cref=\"global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult\"/> that carries this event's identity.</returns>");
                sb.AppendLine($"            public global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult ToOperationResult()");
                sb.AppendLine("            {");
                sb.AppendLine($"                return new global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult(EventId, {evt.ResourceIdExpression});");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Enables implicit conversion to <see cref=\"global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult\"/> so handlers can return event records directly.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            public static implicit operator global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult({evt.RecordName} value)");
                sb.AppendLine("            {");
                sb.AppendLine("                return value.ToOperationResult();");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            /// <summary>");
                sb.AppendLine($"            /// Enables implicit conversion to <see cref=\"global::Blueprint.OperationResult\"/> for pipeline result normalization.");
                sb.AppendLine($"            /// </summary>");
                sb.AppendLine($"            public static implicit operator global::Blueprint.OperationResult({evt.RecordName} value)");
                sb.AppendLine("            {");
                sb.AppendLine("                return value.ToOperationResult();");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource(
            $"{family.ClassName}_ResourceEvents.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private record struct EventShapeResult(bool IsBodyBearing, string? BodyParameterName, string? KeyParameterName, string? BodyTypeFullName);

    private sealed record FamilyResult(FamilyToGenerate? Family, EquatableList<DiagnosticInfo> Diagnostics);

    private sealed record FamilyToGenerate(
        string Namespace,
        string ClassName,
        ImmutableArray<EventRecordInfo> Events) : IEquatable<FamilyToGenerate>
    {
        /// <inheritdoc />
        public bool Equals(FamilyToGenerate? other)
        {
            if (other is null)
            {
                return false;
            }

            return this.Namespace == other.Namespace
                   && this.ClassName == other.ClassName
                   && this.Events.SequenceEqual(other.Events);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (this.Namespace?.GetHashCode() ?? 0) * 397;
                hashCode ^= this.ClassName?.GetHashCode() ?? 0;
                foreach (var evt in this.Events)
                {
                    hashCode = (hashCode * 397) ^ evt.GetHashCode();
                }

                return hashCode;
            }
        }
    }

    private sealed record EventRecordInfo(
        string RecordName,
        string EventId,
        bool IsBodyBearing,
        string? BodyParameterName,
        string? KeyParameterName,
        string ResourceTypeFullName,
        string ContentTypeFullName,
        string KeyTypeFullName,
        string ResourceIdExpression);

    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
    }
}
