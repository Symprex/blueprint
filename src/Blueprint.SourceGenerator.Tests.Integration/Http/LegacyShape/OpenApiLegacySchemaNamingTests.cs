using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that <see cref="OpenApiSchemaNaming.Legacy"/> names types as legacy Blueprint's NJsonSchema generator did.
/// </summary>
/// <remarks>
/// Every expected name is NJsonSchema 11.1's own <c>DefaultSchemaNameGenerator</c> output for the same shape of type
/// (the version legacy Blueprint pinned), run on stand-in types — not worked out from the rules being tested.
/// </remarks>
public class OpenApiLegacySchemaNamingTests
{
    [Theory]
    [InlineData(typeof(LegacyShapeTenantApiResource), "LegacyShapeTenantApiResource")]
    [InlineData(typeof(PagedApiResource<LegacyShapeTenantApiResource>), "PagedApiResourceOfLegacyShapeTenantApiResource")]
    [InlineData(typeof(KeyValuePair<LegacyShapeCountryApiResource, LegacyShapeTenantApiResource>), "KeyValuePairOfLegacyShapeCountryApiResourceAndLegacyShapeTenantApiResource")]
    [InlineData(typeof(PagedApiResource<LegacyShapeListApiResource<LegacyShapeCountryApiResource>>), "PagedApiResourceOfLegacyShapeListApiResourceOfLegacyShapeCountryApiResource")]
    [InlineData(typeof(PagedApiResource<Dictionary<string, LegacyShapeCountryApiResource>>), "PagedApiResourceOfDictionaryOfStringAndLegacyShapeCountryApiResource")]
    [InlineData(typeof(PagedApiResource<short>), "PagedApiResourceOfShort")]
    [InlineData(typeof(PagedApiResource<int>), "PagedApiResourceOfInteger")]
    [InlineData(typeof(PagedApiResource<long>), "PagedApiResourceOfLong")]
    [InlineData(typeof(PagedApiResource<int?>), "PagedApiResourceOfNullableInteger")]
    [InlineData(typeof(PagedApiResource<LegacyShapeStatus?>), "PagedApiResourceOfNullableLegacyShapeStatus")]
    [InlineData(typeof(PagedApiResource<bool>), "PagedApiResourceOfBoolean")]
    [InlineData(typeof(PagedApiResource<Guid>), "PagedApiResourceOfGuid")]
    [InlineData(typeof(PagedApiResource<LegacyShapeCountryApiResource[]>), "PagedApiResourceOfLegacyShapeCountryApiResource[]")]
    [InlineData(typeof(LegacyShapeGatewayRunApiResource), "LegacyShapeRun")]
    [InlineData(typeof(PagedApiResource<LegacyShapeGatewayRunApiResource>), "PagedApiResourceOfLegacyShapeRun")]
    [InlineData(typeof(Outer.Inner), "Inner")]
    [InlineData(typeof(Outer.InnerGeneric<LegacyShapeCountryApiResource>), "InnerGenericOfLegacyShapeCountryApiResource")]
    public void Legacy_naming_gives_the_name_NJsonSchema_gave(Type type, string expected)
    {
        OpenApiSchemaNames.Format(type, OpenApiSchemaNaming.Legacy).Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(PagedApiResource<LegacyShapeTenantApiResource>), "PagedApiResourceOfLegacyShapeTenant")]
    [InlineData(typeof(LegacyShapeTenantApiResource), "LegacyShapeTenantApiResource")]
    public void Default_naming_is_unchanged(Type type, string expected)
    {
        OpenApiSchemaNames.Format(type, OpenApiSchemaNaming.Default).Should().Be(expected);
    }

    /// <summary>Stand-ins for a consumer's nested types.</summary>
    public static class Outer
    {
        public sealed class Inner;

        public sealed class InnerGeneric<T>;
    }
}
