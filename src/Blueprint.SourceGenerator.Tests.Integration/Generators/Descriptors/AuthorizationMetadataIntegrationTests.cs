using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Descriptors;

/// <summary>
/// Proves that the generated descriptor for operations with <c>[Resource]</c> attributes
/// exposes <see cref="OperationAuthorizationFeatureData"/> via feature data at runtime,
/// including correct scope pairs and any-of semantics for multiple attributes.
/// </summary>
public class AuthorizationMetadataIntegrationTests
{
    [Fact]
    public void Descriptor_exposes_authorization_feature_data_with_single_scope_pair()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(SingleResourceQuery));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta);
        hasMeta.Should().BeTrue("operation declares [Resource] and should have authorization metadata");
        meta!.ScopePairs.Should().HaveCount(1);
        meta.ScopePairs[0].Noun.Should().Be("account");
        meta.ScopePairs[0].Verb.Should().Be("read");
    }

    [Fact]
    public void Descriptor_exposes_multiple_scope_pairs_for_any_of_semantics()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(MultiResourceCommand));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta);
        hasMeta.Should().BeTrue("operation declares multiple [Resource] attributes");
        meta!.ScopePairs.Should().HaveCount(2);
        meta.ScopePairs.Should().Contain(new ScopePair("account", "write"));
        meta.ScopePairs.Should().Contain(new ScopePair("tenant", "manage"));
    }

    [Fact]
    public void Descriptor_also_requires_authentication_when_resource_is_declared()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(SingleResourceQuery));
        descriptor.Should().NotBeNull();

        var hasAuth = descriptor!.TryGetFeatureData<OperationAuthenticationFeatureData>(out var auth);
        hasAuth.Should().BeTrue("permissioned operations implicitly require authentication");
        auth!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }

    [Fact]
    public void Descriptor_without_resource_has_no_authorization_feature_data()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(NoResourceQuery));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<OperationAuthorizationFeatureData>(out _);
        hasMeta.Should().BeFalse("operation has no [Resource] attributes");
    }
}

/// <summary>
/// Operation with a single <c>[Resource]</c> for authorization integration testing.
/// </summary>
[RootLink("/authz-test/accounts/{Id}")]
[Resource("account", "read")]
public partial class SingleResourceQuery : IQuery<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(SingleResourceQuery query)
    {
        return Task.FromResult("single-resource");
    }
}

/// <summary>
/// Operation with multiple <c>[Resource]</c> attributes (any-of semantics) for integration testing.
/// </summary>
[RootLink("/authz-test/accounts/{Id}/settings")]
[Resource("account", "write")]
[Resource("tenant", "manage")]
public partial class MultiResourceCommand : ICommand<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the command.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(MultiResourceCommand command)
    {
        return Task.FromResult("multi-resource");
    }
}

/// <summary>
/// Operation without any <c>[Resource]</c> for negative-path integration testing.
/// </summary>
[RootLink("/authz-test/items")]
public partial class NoResourceQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(NoResourceQuery query)
    {
        return Task.FromResult("no-resource");
    }
}
