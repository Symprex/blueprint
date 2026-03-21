using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal interface IValidationFastPathStrategy
{
    bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule);
}
