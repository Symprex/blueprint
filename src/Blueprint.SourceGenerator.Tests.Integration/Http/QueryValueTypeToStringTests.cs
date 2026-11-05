using System.ComponentModel;
using System.Globalization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the typed <c>Http.AppendQueryString</c> skips a value-type query property, whose
/// <c>ToString()</c> is null or empty, as it does a reference type. Signature365's <c>ApiId</c> returns null from
/// <c>ToString()</c> when default; the builder used to throw <see cref="ArgumentNullException"/> from
/// <c>Uri.EscapeDataString</c> before anything was sent, and an empty one used to send <c>key=</c>, which the server rejects.
/// </summary>
public sealed class QueryValueTypeToStringTests
{
    [Fact]
    public void A_default_value_type_whose_ToString_is_null_is_omitted_from_the_query_string()
    {
        var operation = new QueryStructScalarQuery { Count = 0 };

        AppendQuery(operation).Should().Be("/query-struct/scalar?count=0");
    }

    [Fact]
    public void A_value_type_with_a_value_is_sent()
    {
        var operation = new QueryStructScalarQuery { Ref = BoundTenantId.Hydrate("a b"), Count = 3 };

        AppendQuery(operation).Should().Be("/query-struct/scalar?ref=a%20b&count=3");
    }

    [Fact]
    public void A_value_type_whose_ToString_is_empty_is_omitted_from_the_query_string()
    {
        var operation = new QueryStructScalarQuery { Empty = default, Count = 1 };

        AppendQuery(operation).Should().Be("/query-struct/scalar?count=1");
    }

    [Fact]
    public void A_nullable_value_type_holding_a_default_whose_ToString_is_null_is_omitted()
    {
        var operation = new QueryStructScalarQuery { Maybe = default(BoundTenantId), Count = 1 };

        AppendQuery(operation).Should().Be("/query-struct/scalar?count=1");
    }

    [Fact]
    public void A_nullable_value_type_with_a_value_is_sent()
    {
        var operation = new QueryStructScalarQuery { Maybe = BoundTenantId.Hydrate("m"), Count = 1 };

        AppendQuery(operation).Should().Be("/query-struct/scalar?maybe=m&count=1");
    }

    private static string AppendQuery(QueryStructScalarQuery operation)
    {
        return QueryStructScalarQuery.Http.AppendQueryString(operation, QueryStructScalarQuery.Http.BuildUrl(operation)!);
    }
}

/// <summary>A struct whose default <c>ToString()</c> is the empty string, query-bindable by its converter.</summary>
[TypeConverter(typeof(EmptyWhenDefaultIdConverter))]
public readonly struct EmptyWhenDefaultId
{
    private readonly string? value;

    public EmptyWhenDefaultId(string value)
    {
        this.value = value;
    }

    public override string ToString()
    {
        return this.value ?? string.Empty;
    }
}

public sealed class EmptyWhenDefaultIdConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return new EmptyWhenDefaultId((string)value);
    }
}

[RootLink("/query-struct/scalar")]
[HttpGet]
[AllowAnonymous]
public partial class QueryStructScalarQuery : IQuery<string>
{
    public BoundTenantId Ref { get; set; }

    public EmptyWhenDefaultId Empty { get; set; }

    public BoundTenantId? Maybe { get; set; }

    public int Count { get; set; }

    public static string Handle(QueryStructScalarQuery query)
    {
        return "ok";
    }
}
