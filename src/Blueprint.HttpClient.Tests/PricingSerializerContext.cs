using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The serializer context of <see cref="PricingApi"/>. It names the body's members as the transport's own options do not
/// (<c>amount</c> as <c>amountValue</c>, <c>total</c> as <c>totalValue</c>), so that a body shows which of the two it was
/// written or read with, and takes the options' converters as a generated context's metadata does.
/// </summary>
internal sealed class PricingSerializerContext : JsonSerializerContext, IJsonTypeInfoResolver
{
    private static readonly DefaultJsonTypeInfoResolver _resolver = new() { Modifiers = { Rename } };

    private PricingSerializerContext()
        : base(null)
    {
    }

    /// <summary>
    /// Gets the instance a client built over this context uses.
    /// </summary>
    public static PricingSerializerContext Default { get; } = new();

    /// <inheritdoc />
    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

    /// <inheritdoc />
    public override JsonTypeInfo? GetTypeInfo(Type type)
    {
        return this.GetTypeInfo(type, this.Options);
    }

    /// <inheritdoc />
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        return type == typeof(QuotePriceCommand) || type == typeof(PriceQuote) || type == typeof(decimal) || type == typeof(string) ? _resolver.GetTypeInfo(type, options) : null;
    }

    private static void Rename(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.Name is "amount" or "total")
            {
                property.Name += "Value";
            }
        }
    }
}
