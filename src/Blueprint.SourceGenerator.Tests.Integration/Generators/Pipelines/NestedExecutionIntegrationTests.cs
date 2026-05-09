using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Integration tests for nested operation execution and programmatic dispatch via
/// <see cref="ApiOperationContext.CreateNested"/> and <see cref="BlueprintAppExecutor.ExecuteWithNewScopeAsync"/>.
/// </summary>
public class NestedExecutionIntegrationTests
{
    [Fact]
    public void CreateNested_inherits_user_context_and_skips_authorisation()
    {
        // Arrange
        var app = new SampleBlueprintApp();
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var parentDescriptor = app.Operations
            .First(d => d.OperationType == typeof(Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery));

        var parentContext = new ApiOperationContext(
            scope.ServiceProvider,
            app,
            parentDescriptor,
            CancellationToken.None)
        {
            Operation = new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "test-1" },
        };

        // Simulate authenticated user
        var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
        identity.AddClaim(new System.Security.Claims.Claim("sub", "user-123"));
        parentContext.UserPrincipal = new System.Security.Claims.ClaimsPrincipal(identity);

        // Act
        var nested = parentContext.CreateNested(
            new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "test-2" });

        // Assert
        nested.Should().NotBeNull();
        nested.Parent.Should().BeSameAs(parentContext);
        nested.SkipAuthorisation.Should().BeTrue();
        nested.UserPrincipal.Should().BeSameAs(parentContext.UserPrincipal);
        nested.Data.Should().BeSameAs(parentContext.Data);
        nested.Descriptor.OperationType.Should().Be(typeof(Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery));
        nested.Operation.Should().BeOfType<Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery>();
    }

    [Fact]
    public void CreateNested_with_skipAuthorisation_false_does_not_skip()
    {
        // Arrange
        var app = new SampleBlueprintApp();
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var parentDescriptor = app.Operations
            .First(d => d.OperationType == typeof(Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery));

        var parentContext = new ApiOperationContext(
            scope.ServiceProvider,
            app,
            parentDescriptor,
            CancellationToken.None)
        {
            Operation = new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "test-1" },
        };

        // Act
        var nested = parentContext.CreateNested(
            new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "test-2" },
            skipAuthorisation: false);

        // Assert
        nested.SkipAuthorisation.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteWithNewScopeAsync_creates_scope_and_executes_pipeline()
    {
        // Arrange
        var app = new SampleBlueprintApp();
        var executor = new SampleBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();

        var operation = new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "exec-test" };

        // Act — execute the operation through a new scope
        var result = await executor.ExecuteWithNewScopeAsync(operation, sp);

        // Assert — we expect a result (the handler returns NotFound since there's no real data)
        result.Should().NotBeNull();
    }

    [Fact]
    public void CreateNested_throws_for_null_operation()
    {
        // Arrange
        var app = new SampleBlueprintApp();
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var parentDescriptor = app.Operations
            .First(d => d.OperationType == typeof(Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery));

        var parentContext = new ApiOperationContext(
            scope.ServiceProvider,
            app,
            parentDescriptor,
            CancellationToken.None)
        {
            Operation = new Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery { Id = "test-1" },
        };

        // Act & Assert
        var act = () => parentContext.CreateNested(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

