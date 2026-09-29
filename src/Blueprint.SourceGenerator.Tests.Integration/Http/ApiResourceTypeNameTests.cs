using Blueprint.Http;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves <see cref="ApiResource.GetTypeName"/>, which names a resource's <c>$object</c> and the <c>resourceObject</c>
/// and <c>eventId</c> of its resource events, follows legacy Blueprint's rule: remove <c>ApiResource</c>, then
/// <c>Resource</c>, then <c>DTO</c>, each wherever it occurs in the type's name, then camelize.
/// </summary>
/// <remarks>
/// Legacy is <c>Blueprint.Http/ApiResource.cs</c> at Signature365 8b40ecc961 (lines 68-77): three ordinal, case-sensitive
/// <c>Replace</c> calls in that order and <c>Camelize</c>, which PascalCases on underscores and lowers the first
/// character. This stripped one suffix only, so Signature365's <c>EventStoreTestApiResourceWithDoNotAuditProperties</c> was
/// <c>eventStoreTestApiResourceWithDoNotAuditProperties</c>, not legacy's <c>eventStoreTestWithDoNotAuditProperties</c>.
/// The expected names are worked out by hand from that rule. The types are plain classes: only their names matter.
/// </remarks>
public sealed class ApiResourceTypeNameTests
{
    [Theory]
    [InlineData(typeof(EventStoreTestApiResourceWithDoNotAuditProperties), "eventStoreTestWithDoNotAuditProperties")]
    [InlineData(typeof(TenantApiResource), "tenant")]
    [InlineData(typeof(TenantResource), "tenant")]
    [InlineData(typeof(TenantDTO), "tenant")]
    [InlineData(typeof(OrderResourceItem), "orderItem")]
    [InlineData(typeof(UserDTOInfo), "userInfo")]
    [InlineData(typeof(ResourceOwner), "owner")]
    public void A_marker_is_removed_wherever_it_occurs(Type type, string expected)
    {
        ApiResource.GetTypeName(type).Should().Be(expected);
    }

    [Fact]
    public void The_markers_are_removed_in_the_order_ApiResource_then_Resource_then_DTO()
    {
        // Removing ApiResource leaves "ResourceX", so Resource is removed after it. A single pass that removed every marker
        // from the original name would leave "resourceX".
        ApiResource.GetTypeName(typeof(ReApiResourcesourceX)).Should().Be("x");
    }

    [Fact]
    public void DTO_is_matched_case_sensitively()
    {
        ApiResource.GetTypeName(typeof(TenantDto)).Should().Be("tenantDto");
    }

    [Fact]
    public void Only_the_first_character_is_lowered()
    {
        ApiResource.GetTypeName(typeof(SMSMessageApiResource)).Should().Be("sMSMessage");
    }

    [Fact]
    public void An_underscore_is_removed_and_the_next_character_raised_as_legacy_camelized_it()
    {
        ApiResource.GetTypeName(typeof(Foo_barResource)).Should().Be("fooBar");
    }

    [Fact]
    public void A_name_that_is_nothing_but_markers_keeps_its_own_name()
    {
        // Legacy threw on the empty string; a resource named for its marker is named by it instead.
        ApiResource.GetTypeName(typeof(ApiResourceResource)).Should().Be("apiResourceResource");
    }

    [Fact]
    public void A_resource_event_names_its_resource_as_the_resource_does()
    {
        ResourceEvent.GetResourceTypeName(typeof(EventStoreTestApiResourceWithDoNotAuditProperties))
            .Should().Be("eventStoreTestWithDoNotAuditProperties");
    }

    private sealed class EventStoreTestApiResourceWithDoNotAuditProperties;

    private sealed class TenantApiResource;

    private sealed class TenantResource;

    private sealed class TenantDTO;

    private sealed class TenantDto;

    private sealed class OrderResourceItem;

    private sealed class UserDTOInfo;

    private sealed class ResourceOwner;

    private sealed class ReApiResourcesourceX;

    private sealed class SMSMessageApiResource;

    private sealed class Foo_barResource;

    private sealed class ApiResourceResource;
}
