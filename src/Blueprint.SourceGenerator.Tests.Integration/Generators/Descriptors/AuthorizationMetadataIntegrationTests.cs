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

    [Fact]
    public void Descriptor_inherits_scope_pairs_from_FromResource_target()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(InheritedResourceQuery));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta);
        hasMeta.Should().BeTrue("[FromResource] should produce authorization metadata");
        meta!.ScopePairs.Should().HaveCount(1);
        meta.ScopePairs[0].Noun.Should().Be("account");
        meta.ScopePairs[0].Verb.Should().Be("read");
    }

    [Fact]
    public void Descriptor_inheriting_FromResource_also_requires_authentication()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(InheritedResourceQuery));
        descriptor.Should().NotBeNull();

        var hasAuth = descriptor!.TryGetFeatureData<OperationAuthenticationFeatureData>(out var auth);
        hasAuth.Should().BeTrue("[FromResource] operations implicitly require authentication");
        auth!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }

    [Theory]
    [InlineData(typeof(SingleResourceQuery), OperationAccessMode.Permissioned)]
    [InlineData(typeof(InheritedResourceQuery), OperationAccessMode.Permissioned)]
    [InlineData(typeof(NoResourceQuery), OperationAccessMode.Anonymous)]
    [InlineData(typeof(ExplicitlyAuthenticatedQuery), OperationAccessMode.Authenticated)]
    [InlineData(typeof(AuthenticatedAndPermissionedQuery), OperationAccessMode.Permissioned)]
    [InlineData(typeof(NonHttpAuthenticatedCommand), OperationAccessMode.Authenticated)]
    public void Descriptor_exposes_the_declared_access_mode(Type operationType, OperationAccessMode expected)
    {
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(d => d.OperationType == operationType);

        descriptor.TryGetFeatureData<OperationAccessFeatureData>(out var access).Should().BeTrue();
        access!.Mode.Should().Be(expected);
    }

    [Fact]
    public void Descriptor_with_no_access_declaration_has_no_access_feature_data()
    {
        // An undeclared HTTP operation still requires authentication by default, so its authentication feature data
        // cannot tell it from [MustBeAuthenticated]; the access feature data's absence does.
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(static d => d.OperationType == typeof(UndeclaredAccessQuery));

        descriptor.TryGetFeatureData<OperationAccessFeatureData>(out _).Should().BeFalse();
        descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out var auth).Should().BeTrue();
        auth!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }

    [Fact]
    public void Descriptor_records_where_FromResource_inherited_its_scope_pairs_from()
    {
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(static d => d.OperationType == typeof(InheritedResourceQuery));

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta).Should().BeTrue();
        meta!.InheritedFrom.Should().Be(typeof(SingleResourceQuery));
        meta.InheritedTemplateOverride.Should().BeNull();
    }

    [Fact]
    public void Descriptor_records_a_FromResource_template_override_and_applies_it_to_every_inherited_pair()
    {
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(static d => d.OperationType == typeof(OverriddenInheritedResourceQuery));

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta).Should().BeTrue();
        meta!.InheritedFrom.Should().Be(typeof(MultiResourceCommand));
        meta.InheritedTemplateOverride.Should().Be("/accounts/{Id}");
        meta.ScopePairs.Should().BeEquivalentTo(new[]
        {
            new ScopePair("account", "write", "/accounts/{Id}"),
            new ScopePair("tenant", "manage", "/accounts/{Id}"),
        });
    }

    [Theory]
    [InlineData(typeof(EmptyOverrideInheritedResourceQuery), "")]
    [InlineData(typeof(WhitespaceOverrideInheritedResourceQuery), "  ")]
    public void Descriptor_records_a_blank_FromResource_override_as_declared_but_keeps_the_source_templates(Type operationType, string declared)
    {
        // The attribute holds the blank override as written, so the declaration data does too; a blank override is not a
        // template, so the inherited pairs keep their own
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(d => d.OperationType == operationType);

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta).Should().BeTrue();
        meta!.InheritedTemplateOverride.Should().Be(declared);
        meta.ScopePairs.Should().BeEquivalentTo(new[] { new ScopePair("account", "read") });
    }

    [Fact]
    public void Descriptor_of_a_direct_Resource_records_no_inheritance()
    {
        var descriptor = new IntegrationTestBlueprintApp().Operations.Single(static d => d.OperationType == typeof(MultiResourceCommand));

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta).Should().BeTrue();
        meta!.InheritedFrom.Should().BeNull();
        meta.InheritedTemplateOverride.Should().BeNull();
    }

    [Fact]
    public void Generated_authorization_metadata_matches_reflection_over_the_attributes_for_every_operation()
    {
        // The proof that an app can read the generated data instead of reflecting over the attributes itself
        // (Symprex/blueprint#83): for every operation in the app, the two agree.
        var operations = new IntegrationTestBlueprintApp().Operations;

        operations.Should().NotBeEmpty();

        foreach (var descriptor in operations)
        {
            var type = descriptor.OperationType;
            var because = $"{type.Name}'s generated metadata should match its attributes";

            var expectedMode = ExpectedAccessMode(type);
            var hasAccess = descriptor.TryGetFeatureData<OperationAccessFeatureData>(out var access);
            hasAccess.Should().Be(expectedMode is not null, because);
            access?.Mode.Should().Be(expectedMode, because);

            var fromResource = type.GetCustomAttributes(typeof(FromResourceAttribute), false).OfType<FromResourceAttribute>().SingleOrDefault();
            var direct = type.GetCustomAttributes(typeof(ResourceAttribute), false).OfType<ResourceAttribute>().ToArray();
            var hasMeta = descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var meta);

            if (fromResource is null && direct.Length == 0)
            {
                hasMeta.Should().BeFalse(because);
                continue;
            }

            hasMeta.Should().BeTrue(because);

            // A blank override is recorded as declared but is not a template, so the inherited pairs keep their own
            var effectiveOverride = string.IsNullOrWhiteSpace(fromResource?.ResourceKeyTemplate) ? null : fromResource!.ResourceKeyTemplate;
            var expectedPairs = fromResource is null
                ? direct.Select(static a => new ScopePair(a.Noun, a.Verb, a.ResourceKeyTemplate))
                : fromResource.SourceOperation.GetCustomAttributes(typeof(ResourceAttribute), false).OfType<ResourceAttribute>()
                    .Select(a => new ScopePair(a.Noun, a.Verb, effectiveOverride ?? a.ResourceKeyTemplate));

            meta!.ScopePairs.Should().BeEquivalentTo(expectedPairs, because);
            meta.InheritedFrom.Should().Be(fromResource?.SourceOperation, because);
            meta.InheritedTemplateOverride.Should().Be(fromResource?.ResourceKeyTemplate, because);
        }
    }

    private static OperationAccessMode? ExpectedAccessMode(Type type)
    {
        if (type.IsDefined(typeof(ResourceAttribute), false) || type.IsDefined(typeof(FromResourceAttribute), false))
        {
            return OperationAccessMode.Permissioned;
        }

        if (type.IsDefined(typeof(AllowAnonymousAttribute), false))
        {
            return OperationAccessMode.Anonymous;
        }

        if (type.IsDefined(typeof(MustBeAuthenticatedAttribute), false))
        {
            return OperationAccessMode.Authenticated;
        }

        return null;
    }
}

/// <summary>
/// Operation that inherits <see cref="MultiResourceCommand"/>'s two scope pairs with a template of its own.
/// </summary>
[RootLink("/authz-test/accounts/{Id}/overridden")]
[FromResource(typeof(MultiResourceCommand), "/accounts/{Id}")]
public partial class OverriddenInheritedResourceQuery : IQuery<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(OverriddenInheritedResourceQuery query)
    {
        return Task.FromResult("overridden-inherited-resource");
    }
}

/// <summary>
/// Operation that inherits <see cref="SingleResourceQuery"/>'s scope pair with an empty override.
/// </summary>
[RootLink("/authz-test/accounts/{Id}/empty-override")]
[FromResource(typeof(SingleResourceQuery), "")]
public partial class EmptyOverrideInheritedResourceQuery : IQuery<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(EmptyOverrideInheritedResourceQuery query)
    {
        return Task.FromResult("empty-override");
    }
}

/// <summary>
/// Operation that inherits <see cref="SingleResourceQuery"/>'s scope pair with a whitespace override.
/// </summary>
[RootLink("/authz-test/accounts/{Id}/whitespace-override")]
[FromResource(typeof(SingleResourceQuery), "  ")]
public partial class WhitespaceOverrideInheritedResourceQuery : IQuery<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(WhitespaceOverrideInheritedResourceQuery query)
    {
        return Task.FromResult("whitespace-override");
    }
}

/// <summary>
/// Operation declaring <c>[MustBeAuthenticated]</c> that no host exposes over HTTP. BPAU001 warns that no HTTP check is
/// generated for it, but the declaration is still recorded.
/// </summary>
#pragma warning disable BPAU001 // The point of this operation is a [MustBeAuthenticated] with no HTTP host
[MustBeAuthenticated]
public partial class NonHttpAuthenticatedCommand : ICommand<string>
#pragma warning restore BPAU001
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(NonHttpAuthenticatedCommand command)
    {
        return Task.FromResult("non-http-authenticated");
    }
}

/// <summary>
/// Operation that declares <c>[MustBeAuthenticated]</c> and no scope pairs.
/// </summary>
[MustBeAuthenticated]
[RootLink("/authz-test/explicitly-authenticated")]
public partial class ExplicitlyAuthenticatedQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(ExplicitlyAuthenticatedQuery query)
    {
        return Task.FromResult("explicitly-authenticated");
    }
}

/// <summary>
/// Operation that declares <c>[MustBeAuthenticated]</c> beside a scope pair, which makes it permissioned.
/// </summary>
[MustBeAuthenticated]
[Resource("account", "read")]
[RootLink("/authz-test/authenticated-and-permissioned")]
public partial class AuthenticatedAndPermissionedQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(AuthenticatedAndPermissionedQuery query)
    {
        return Task.FromResult("authenticated-and-permissioned");
    }
}

/// <summary>
/// Operation that declares no access attribute at all.
/// </summary>
[RootLink("/authz-test/undeclared")]
public partial class UndeclaredAccessQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(UndeclaredAccessQuery query)
    {
        return Task.FromResult("undeclared");
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
[AllowAnonymous]
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

/// <summary>
/// Operation that inherits scope pairs from <see cref="SingleResourceQuery"/> via
/// <c>[FromResource]</c> for integration testing of compile-time resource inheritance.
/// </summary>
[RootLink("/authz-test/accounts/{Id}/inherited")]
[FromResource(typeof(SingleResourceQuery))]
public partial class InheritedResourceQuery : IQuery<string>
{
    /// <summary>Gets or sets the account ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A test result.</returns>
    public static Task<string> HandleAsync(InheritedResourceQuery query)
    {
        return Task.FromResult("inherited-resource");
    }
}
