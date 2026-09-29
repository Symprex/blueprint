using Blueprint.Model;
using Blueprint.Model.Attributes;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a resource's generated <c>$object</c> is the name <see cref="ApiResource.GetTypeName"/> gives it, which is
/// the <c>eventId</c> and <c>resourceObject</c> of its events, for a name with an acronym as for any other.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>origin/master</c>, <c>Blueprint.Http/ApiResource.cs:28-31</c>, <c>:68-77</c>) set <c>$object</c>
/// to <c>GetTypeName(GetType())</c>, which lowers the first character only: <c>SMSAlertApiResource</c> was
/// <c>sMSAlert</c>. The generated override lowered the whole leading acronym (<c>smsAlert</c>), so the two disagreed.
/// An explicit name is different: legacy's <c>GatewayImportRunApiResource</c> set its own <c>$object</c> from
/// <c>[JsonSchema("ImportRun")]</c> in its constructor (<c>importRun</c>) while <c>GetTypeName</c>, and so its
/// <c>resourceObject</c>, stayed <c>gatewayImportRun</c>. <c>[ResourceObjectName]</c> is the same override.
/// </remarks>
public sealed class ApiResourceObjectAgreesWithTypeNameTests
{
    [Theory]
    [InlineData(typeof(SMSAlertApiResource), "sMSAlert")]
    [InlineData(typeof(APIKeyResource), "aPIKey")]
    [InlineData(typeof(OrderApiResourceLine), "orderLine")]
    [InlineData(typeof(Report_summaryApiResource), "reportSummary")]
    public void A_resources_object_is_its_type_name(Type type, string expected)
    {
        var resource = (ApiResource)Activator.CreateInstance(type)!;

        resource.Object.Should().Be(expected);
        resource.Object.Should().Be(ApiResource.GetTypeName(type));
    }

    [Fact]
    public void An_explicit_object_name_overrides_the_object_and_not_the_type_name_as_legacys_constructor_override_did()
    {
        var resource = new AcronymGatewayRunApiResource();

        resource.Object.Should().Be("gatewayRun");
        ApiResource.GetTypeName(typeof(AcronymGatewayRunApiResource)).Should().Be("acronymGatewayRun");
    }
}

/// <summary>A resource whose name starts with an acronym.</summary>
public partial class SMSAlertApiResource : ApiResource;

/// <summary>A resource named by an acronym and a marker.</summary>
public partial class APIKeyResource : ApiResource;

/// <summary>A resource with a marker inside its name.</summary>
public partial class OrderApiResourceLine : ApiResource;

/// <summary>A resource with an underscore in its name.</summary>
public partial class Report_summaryApiResource : ApiResource;

/// <summary>A resource with an explicit object name.</summary>
[ResourceObjectName("gatewayRun")]
public partial class AcronymGatewayRunApiResource : ApiResource;
