using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Attempts to translate one validation attribute into a simple generated rule so the pipeline can avoid whole-object
/// runtime validation when the attribute shape is deterministic.
/// </summary>
internal interface IValidationFastPathStrategy
{
    /// <summary>
    /// Tries to convert the supplied validation attribute into a generated fast-path rule.
    /// </summary>
    /// <param name="property">The operation property the attribute is applied to.</param>
    /// <param name="propertyAccess">The generated-code expression used to read the property value.</param>
    /// <param name="attribute">The attribute instance to translate.</param>
    /// <param name="rule">Receives the generated validation rule when translation succeeds.</param>
    /// <returns><see langword="true"/> when the attribute can be rendered as a generated fast-path rule.</returns>
    bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule);
}
