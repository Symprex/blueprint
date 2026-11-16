using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// The class-level diagnostics of one class marked <c>[ForwardsTo&lt;TInner&gt;]</c> (D10, D16), reported by the
/// descriptor generator whether or not the class is an operation yet: D11's binding diagnostics, which apply to both
/// modes, and the type-match and supply checks of declared mode.
/// </summary>
/// <remarks>
/// A required member (the <c>required</c> modifier) the forward does not set is BPGW019's, which the pipeline generator
/// reports because the forward would not compile, and which no map hook can help: <c>new TInner { … }</c> has to set it.
/// </remarks>
/// <param name="Diagnostics">The diagnostics, empty when there are none.</param>
internal sealed record DeclaredForwardAnalysis(EquatableList<DiagnosticInfo> Diagnostics)
{
    /// <summary>
    /// Analyses one class marked <c>[ForwardsTo&lt;TInner&gt;]</c>.
    /// </summary>
    /// <param name="metadataName">The class's fully qualified metadata name.</param>
    /// <param name="compilation">The compilation the class is read from.</param>
    /// <returns>The analysis, or <see langword="null"/> when the class or its inner operation cannot be resolved.</returns>
    public static DeclaredForwardAnalysis? Analyse(string metadataName, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is not { } type
            || DeclaredForward.FindForwardedType(type, out var attribute) is not { } inner)
        {
            return null;
        }

        var location = attribute?.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : LocationInfo.CreateFrom(type.Locations.First());

        var diagnostics = new List<DiagnosticInfo>();
        if (OperationPropertyModelBuilder.FindExposedType(type, out _) is not null)
        {
            diagnostics.Add(new DiagnosticInfo(GatewayDiagnosticDescriptors.ExposeAndForwardsTo, location, [type.Name]));
        }

        if (!type.ImplementsInterface(ExposedOperationAnalysis.OperationInterfaceMetadataName))
        {
            diagnostics.Add(new DiagnosticInfo(
                GatewayDiagnosticDescriptors.MissingOperationInterface,
                location,
                [type.Name, inner.Name, ExposedOperationAnalysis.SuggestInterface(inner), "ForwardsTo"]));
        }

        diagnostics.AddRange(GatewayBindings.Analyse(type, inner, location));

        var bindings = GatewayBindings.Resolve(type, inner);
        var innerSettable = DeclaredForward.GetSettableProperties(inner);
        var renameTargets = DeclaredForward.GetRenameTargets(type);
        var suppliers = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var property in OperationPropertyModelBuilder.GetProperties(type))
        {
            var member = property.Member;
            var forwardAs = DeclaredForward.FindForwardAs(member, out _);
            var innerName = DeclaredForward.GetPairedName(member, renameTargets);
            if (innerName is null)
            {
                // A property another rename displaces is the gateway's own; a rename with no target names nothing.
                if (forwardAs is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        GatewayDiagnosticDescriptors.ForwardAsTargetNotOnInnerOperation,
                        GetLocation(forwardAs, member),
                        [type.Name, member.Name, "null", inner.Name]));
                }

                continue;
            }

            if (!innerSettable.TryGetValue(innerName, out var counterpart))
            {
                if (forwardAs is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        GatewayDiagnosticDescriptors.ForwardAsTargetNotOnInnerOperation,
                        GetLocation(forwardAs, member),
                        [type.Name, member.Name, innerName, inner.Name]));
                }

                continue;
            }

            // A same-named property a binding fills is BPGW021's; a rename onto one supplies it twice.
            if (bindings.Any(b => b.PropertyName == innerName))
            {
                if (forwardAs is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        GatewayDiagnosticDescriptors.InnerPropertyForwardedTwice,
                        GetLocation(forwardAs, member),
                        [type.Name, inner.Name, innerName, "'" + member.Name + "' and a binding"]));
                }

                continue;
            }

            if (!suppliers.TryGetValue(innerName, out var names))
            {
                names = [];
                suppliers.Add(innerName, names);
            }

            names.Add(member.Name);

            if (!DeclaredForward.HasCounterpartType(member, counterpart))
            {
                diagnostics.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.DeclaredPropertyTypeMismatch,
                    property.Info.Location,
                    [type.Name, member.Name, member.Type.ToDisplayString(), inner.Name, innerName, counterpart.Type.ToDisplayString()]));
            }
        }

        foreach (var supplied in suppliers)
        {
            if (supplied.Value.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.InnerPropertyForwardedTwice,
                    location,
                    [type.Name, inner.Name, supplied.Key, string.Join(", ", supplied.Value.Select(static n => "'" + n + "'"))]));
            }
        }

        var hasMapHook = GatewayMapHook.Find(type, inner, diagnostics: null) is not null;
        foreach (var innerProperty in innerSettable.Values)
        {
            // A required member is BPGW019's, and a hidden [Required] one is BPGW009's.
            if (innerProperty.IsRequired
                || !IsMarkedRequired(innerProperty)
                || suppliers.ContainsKey(innerProperty.Name)
                || bindings.Any(b => b.PropertyName == innerProperty.Name))
            {
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                hasMapHook ? GatewayDiagnosticDescriptors.RequiredInnerPropertyLeftToMapHook : GatewayDiagnosticDescriptors.RequiredInnerPropertyNotSupplied,
                location,
                [type.Name, inner.Name, innerProperty.Name]));
        }

        return new DeclaredForwardAnalysis(EquatableList<DiagnosticInfo>.TakeOwnership(diagnostics));
    }

    private static bool IsMarkedRequired(IPropertySymbol property)
    {
        return DeclaredForward.GetEffectiveAttributes(property).Any(static a => a.AttributeClass is { } attributeClass
            && (attributeClass.ToFullMetadataName() == "System.ComponentModel.DataAnnotations.RequiredAttribute"
                || attributeClass.InheritsFrom("System.ComponentModel.DataAnnotations.RequiredAttribute")));
    }

    private static LocationInfo? GetLocation(AttributeData attribute, IPropertySymbol property)
    {
        return attribute.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : property.Locations.FirstOrDefault() is { } location ? LocationInfo.CreateFrom(location) : null;
    }
}
