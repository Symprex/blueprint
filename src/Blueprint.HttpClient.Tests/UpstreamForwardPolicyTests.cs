using System.Net;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// How a gateway forward reads a gateway operation's and its app's <see cref="UpstreamCredentialAttribute"/> and
/// <see cref="TranslateErrorAttribute"/>: the cases the sample gateway does not declare.
/// </summary>
public sealed class UpstreamForwardPolicyTests
{
    [Fact]
    public void An_apps_translation_applies_to_an_operation_that_does_not_declare_one()
    {
        var policy = UpstreamForwardPolicy.For(typeof(PlainOperation), typeof(TranslatingApp));

        policy.FindTranslation("service_unavailable").Should().BeSameAs(TranslatingApp.Unavailable);
    }

    [Fact]
    public void An_operations_translation_wins_over_the_apps_for_the_same_problem_type()
    {
        var policy = UpstreamForwardPolicy.For(typeof(TranslatingOperation), typeof(TranslatingApp));

        policy.FindTranslation("service_unavailable").Should().BeSameAs(TranslatingOperation.OwnUnavailable);
        policy.FindTranslation("not_found").Should().BeSameAs(TranslatingOperation.Missing);
    }

    [Fact]
    public void A_problem_type_nothing_names_has_no_translation()
    {
        var policy = UpstreamForwardPolicy.For(typeof(TranslatingOperation), typeof(TranslatingApp));

        policy.FindTranslation("conflict").Should().BeNull();
        policy.FindTranslation(null).Should().BeNull();
    }

    [Fact]
    public void A_translation_to_a_member_that_is_not_a_static_factory_fails_naming_it()
    {
        var act = () => UpstreamForwardPolicy.For(typeof(MisnamedTranslationOperation), typeof(PlainApp));

        act.Should().Throw<InvalidOperationException>().WithMessage("*NoSuchFactory*MisnamedTranslationOperation*");
    }

    [Fact]
    public void A_strategys_own_attribute_is_the_one_the_policy_carries_with_its_arguments()
    {
        var policy = UpstreamForwardPolicy.For(typeof(TenantCredentialOperation), typeof(PlainApp));

        policy.CredentialAttribute.Should().BeOfType<TenantCredentialAttribute>()
            .Which.TenantIdProperty.Should().Be("TenantId");
        policy.CredentialAttribute!.CredentialType.Should().Be<TenantStrategy>();
    }

    [Fact]
    public void An_operation_with_no_credential_attribute_has_none()
    {
        UpstreamForwardPolicy.For(typeof(PlainOperation), typeof(PlainApp)).CredentialAttribute.Should().BeNull();
    }

    [Fact]
    public void An_operation_with_two_credential_attributes_fails()
    {
        var act = () => UpstreamForwardPolicy.For(typeof(TwoCredentialsOperation), typeof(PlainApp));

        act.Should().Throw<InvalidOperationException>().WithMessage("*TwoCredentialsOperation*");
    }

    private sealed class PlainApp;

    [TranslateError("service_unavailable", nameof(Unavailable))]
    private sealed class TranslatingApp
    {
        public static readonly ApiExceptionFactory Unavailable = new("App unavailable", "app_unavailable", HttpStatusCode.BadGateway);
    }

    private sealed class PlainOperation;

    [TranslateError("service_unavailable", nameof(OwnUnavailable))]
    [TranslateError("not_found", nameof(Missing))]
    private sealed class TranslatingOperation
    {
        public static readonly ApiExceptionFactory OwnUnavailable = new("Op unavailable", "op_unavailable", HttpStatusCode.ServiceUnavailable);

        public static ApiExceptionFactory Missing { get; } = new("Missing", "missing", HttpStatusCode.NotFound);
    }

    [TranslateError("not_found", "NoSuchFactory")]
    private sealed class MisnamedTranslationOperation;

    [TenantCredential(TenantIdProperty = "TenantId")]
    private sealed class TenantCredentialOperation;

    [UpstreamCredential<TenantStrategy>]
    [TenantCredential(TenantIdProperty = "TenantId")]
    private sealed class TwoCredentialsOperation;

    private sealed class TenantCredentialAttribute : UpstreamCredentialAttribute<TenantStrategy>
    {
        public string? TenantIdProperty { get; set; }
    }

    private sealed class TenantStrategy : IUpstreamCredentialStrategy
    {
        public IUpstreamCredential CreateCredential(ApiOperationContext context, Blueprint.Model.IOperation inner, UpstreamCredentialAttribute attribute)
        {
            return new BearerTokenCredential("tenant");
        }
    }
}
