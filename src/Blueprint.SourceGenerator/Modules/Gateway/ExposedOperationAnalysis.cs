using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// What the descriptor generator needs about one class marked <c>[Expose&lt;TInner&gt;]</c>, whether or not it is an
/// operation yet: its property model, to emit the contributed properties, and the class-level diagnostics of D9 and D26.
/// </summary>
/// <param name="Model">The class's property model.</param>
/// <param name="OperationName">The class's name, for messages.</param>
/// <param name="InnerName">The inner operation's name, for messages.</param>
/// <param name="AttributeLocation">The <c>[Expose&lt;TInner&gt;]</c> attribute, where the diagnostics are reported.</param>
/// <param name="MissingInterface">BPGW006, when the class declares no operation interface.</param>
internal sealed record ExposedOperationAnalysis(
    OperationPropertyModel Model,
    string OperationName,
    string InnerName,
    LocationInfo? AttributeLocation,
    DiagnosticInfo? MissingInterface)
{
    private const string OperationInterfaceMetadataName = "Blueprint.Model.IOperation";

    /// <summary>
    /// Analyses one class marked <c>[Expose&lt;TInner&gt;]</c>.
    /// </summary>
    /// <param name="metadataName">The class's fully qualified metadata name.</param>
    /// <param name="compilation">The compilation the class is read from.</param>
    /// <returns>The analysis, or <see langword="null"/> when the class or its inner operation cannot be resolved.</returns>
    public static ExposedOperationAnalysis? Analyse(string metadataName, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is not { } type
            || OperationPropertyModelBuilder.FindExposedType(type, out var attribute) is not { } inner)
        {
            return null;
        }

        var location = attribute?.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : null;

        DiagnosticInfo? missingInterface = null;
        if (!type.ImplementsInterface(OperationInterfaceMetadataName))
        {
            missingInterface = new DiagnosticInfo(
                GatewayDiagnosticDescriptors.MissingOperationInterface,
                location,
                [type.Name, inner.Name, SuggestInterface(inner)]);
        }

        return new ExposedOperationAnalysis(OperationPropertyModelBuilder.Build(type), type.Name, inner.Name, location, missingInterface);
    }

    /// <summary>
    /// Gets the diagnostics to report for the class.
    /// </summary>
    /// <param name="allowExpose">Whether the project allows <c>[Expose&lt;TInner&gt;]</c> (D26).</param>
    /// <returns>The diagnostics, empty when there are none.</returns>
    public EquatableList<DiagnosticInfo> GetDiagnostics(bool allowExpose)
    {
        var diagnostics = new List<DiagnosticInfo>(2);
        if (!allowExpose)
        {
            diagnostics.Add(new DiagnosticInfo(GatewayDiagnosticDescriptors.ExposeNotAllowed, this.AttributeLocation, [this.OperationName, this.InnerName]));
        }

        if (this.MissingInterface is not null)
        {
            diagnostics.Add(this.MissingInterface);
        }

        return EquatableList<DiagnosticInfo>.TakeOwnership(diagnostics);
    }

    /// <summary>
    /// Suggests the operation interface the class should declare: the inner operation's own, which carries its result.
    /// </summary>
    private static string SuggestInterface(INamedTypeSymbol inner)
    {
        foreach (var candidate in inner.AllInterfaces)
        {
            if (candidate is { IsGenericType: true, TypeArguments.Length: 1 }
                && candidate.Name is "IQuery" or "ICommand"
                && candidate.IsInNamespace("Blueprint", "Model"))
            {
                return candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::Blueprint.Model.", string.Empty);
            }
        }

        return OperationResultContractHelper.GetDeclaredOperationResultType(inner) is { } result
            ? "IQuery<" + result.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">"
            : "IQuery";
    }
}
