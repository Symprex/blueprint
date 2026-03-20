using Blueprint.SourceGenerator.Planning;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Planning;

public class VariableRegistryTests
{
    [Fact]
    public void When_duplicate_unique_producer_registered_then_throws()
    {
        var registry = new VariableRegistry();
        var key = new VariableKey("global::Marten.IDocumentSession", "document-session");

        registry.RegisterVariable(
            new VariableRegistration(
                key,
                ProducerId: "persistence",
                AvailableFrom: PipelineStage.Execution,
                SourceKind: VariableSourceKind.Service,
                Lifetime: VariableLifetime.Local));

        var act = () => registry.RegisterVariable(
            new VariableRegistration(
                key,
                ProducerId: "outbox",
                AvailableFrom: PipelineStage.Execution,
                SourceKind: VariableSourceKind.Service,
                Lifetime: VariableLifetime.Local));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*document-session*");
    }

    [Fact]
    public void When_requirements_registered_then_they_are_tracked()
    {
        var registry = new VariableRegistry();
        var required = new VariableRequirement(
            new VariableKey("global::System.Security.Claims.ClaimsIdentity", "identity"),
            ConsumerId: "auth",
            RequiredBy: PipelineStage.Authentication,
            IsRequired: true);
        var optional = new VariableRequirement(
            new VariableKey("global::Rebus.Transport.ITransactionContext", "transaction-scope"),
            ConsumerId: "outbox",
            RequiredBy: PipelineStage.PostExecution,
            IsRequired: false);

        registry.RequireVariable(required);
        registry.RequireVariable(optional);

        registry.Requirements.Should().Equal(required, optional);
    }

    [Fact]
    public void When_multiple_producers_allowed_then_both_are_tracked()
    {
        var registry = new VariableRegistry();
        var key = new VariableKey("global::System.String", "header-value");

        registry.RegisterVariable(
            new VariableRegistration(
                key,
                ProducerId: "http",
                AvailableFrom: PipelineStage.Population,
                SourceKind: VariableSourceKind.Context,
                Lifetime: VariableLifetime.ContextLookup,
                AllowMultiple: true));
        registry.RegisterVariable(
            new VariableRegistration(
                key,
                ProducerId: "messaging",
                AvailableFrom: PipelineStage.Population,
                SourceKind: VariableSourceKind.Context,
                Lifetime: VariableLifetime.ContextLookup,
                AllowMultiple: true));

        registry.Registrations.Select(p => p.ProducerId).Should().Equal("http", "messaging");
    }

    [Fact]
    public void When_unique_provisions_use_different_purposes_then_both_are_allowed()
    {
        var registry = new VariableRegistry();

        registry.RegisterVariable(
            new VariableRegistration(
                new VariableKey("global::System.String", "tenant-id"),
                ProducerId: "tenant",
                AvailableFrom: PipelineStage.Setup,
                SourceKind: VariableSourceKind.Context,
                Lifetime: VariableLifetime.ContextLookup));
        registry.RegisterVariable(
            new VariableRegistration(
                new VariableKey("global::System.String", "correlation-id"),
                ProducerId: "correlation",
                AvailableFrom: PipelineStage.Setup,
                SourceKind: VariableSourceKind.Context,
                Lifetime: VariableLifetime.ContextLookup));

        registry.Registrations.Select(p => p.Key.Purpose).Should().Equal("tenant-id", "correlation-id");
    }
}
