using System.Diagnostics.CodeAnalysis;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// A strongly-typed template identifier that implements <see cref="IParsable{TSelf}"/>
/// for compile-time safe route and query parameter binding. The generated pipeline will
/// call <c>TemplateId.TryParse</c> directly instead of using reflection or generic
/// type-test fallbacks.
/// </summary>
/// <remarks>
/// This demonstrates the recommended pattern for domain-typed IDs in Blueprint operations.
/// When used in a <c>[RootLink]</c> or <c>[SelfLink]</c> route, the source generator
/// emits a direct <c>TryParse</c> call, producing a <see cref="Blueprint.Runtime.Exec.BindingFailureResult"/>
/// (HTTP 400) if the input cannot be parsed.
/// </remarks>
public readonly record struct TemplateId(Guid Value) : IParsable<TemplateId>
{
    /// <inheritdoc />
    public static TemplateId Parse(string s, IFormatProvider? provider)
    {
        return new TemplateId(Guid.Parse(s));
    }

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TemplateId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new TemplateId(guid);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return this.Value.ToString();
    }
}
