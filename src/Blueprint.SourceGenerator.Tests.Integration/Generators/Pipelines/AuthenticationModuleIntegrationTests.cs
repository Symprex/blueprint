using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves generated authentication descriptor metadata end-to-end by calling
/// <c>Configure()</c> on operations compiled with the real source generator.
/// </summary>
public class AuthenticationModuleIntegrationTests
{
    [Fact]
    public void When_MustBeAuthenticated_then_descriptor_has_Authenticated_requirement()
    {
        var descriptor = AuthenticatedIntegrationQuery.Configure();

        descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out var authData)
            .Should().BeTrue("descriptor should contain authentication feature data");

        authData!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }

    [Fact]
    public void When_no_auth_attribute_then_descriptor_defaults_to_authenticated()
    {
        var descriptor = UnauthenticatedIntegrationQuery.Configure();

        descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out var authData)
            .Should().BeTrue("HTTP operations default to authenticated (legacy Blueprint parity)");

        authData!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }
}

/// <summary>
/// HTTP-exposed query with <c>[MustBeAuthenticated]</c> for integration testing.
/// </summary>
[Blueprint.Model.Attributes.MustBeAuthenticated]
[Blueprint.Model.Attributes.RootLink("/auth-test/items")]
public partial class AuthenticatedIntegrationQuery : IQuery<string>
{
    public static Task<string> HandleAsync(AuthenticatedIntegrationQuery operation)
    {
        return Task.FromResult("authenticated");
    }
}

/// <summary>
/// HTTP-exposed query without auth attributes — defaults to authenticated (legacy Blueprint parity).
/// Use <c>[AllowAnonymous]</c> to opt out of authentication.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/auth-test/public")]
public partial class UnauthenticatedIntegrationQuery : IQuery<string>
{
    public static Task<string> HandleAsync(UnauthenticatedIntegrationQuery operation)
    {
        return Task.FromResult("public");
    }
}
