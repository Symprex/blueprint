using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the <c>UnitOfWorkModule</c> coordinates persistence saves through the generated
/// pipeline variable sharing model: save-changes is called on command and query success, and skipped on
/// validation failure and for <c>[DoNotSaveChanges]</c>.
/// </summary>
public class UnitOfWorkModuleIntegrationTests
{
    [Fact]
    public async Task When_command_handler_succeeds_then_unit_of_work_saves_changes()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var unitOfWork = new TestUnitOfWork();
        var serviceProvider = new PersistenceTestServiceProvider(unitOfWork);

        var pipeline = executor.GetPipeline(typeof(PersistableCommand));
        var context = new ApiOperationContext(serviceProvider, app, PersistableCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((PersistableCommand)context.Operation).Name = "valid-name";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();
        unitOfWork.SaveChangesCallCount.Should().Be(1, "save-changes should be called exactly once after successful command execution");
    }

    [Fact]
    public async Task When_command_fails_validation_then_unit_of_work_does_not_save()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var unitOfWork = new TestUnitOfWork();
        var serviceProvider = new PersistenceTestServiceProvider(unitOfWork);

        var pipeline = executor.GetPipeline(typeof(PersistableCommand));
        var context = new ApiOperationContext(serviceProvider, app, PersistableCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        // Leave Name null to trigger Required validation failure
        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        unitOfWork.SaveChangesCallCount.Should().Be(0, "save-changes should NOT be called when validation fails");
    }

    [Fact]
    public async Task When_no_unit_of_work_registered_then_command_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var serviceProvider = new PersistenceTestServiceProvider(unitOfWork: null);

        var pipeline = executor.GetPipeline(typeof(PersistableCommand));
        var context = new ApiOperationContext(serviceProvider, app, PersistableCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((PersistableCommand)context.Operation).Name = "no-uow";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task When_query_operation_succeeds_then_unit_of_work_saves_changes()
    {
        // R138: legacy's SaveMartenChangesMiddleware saved after any operation without [DoNotSaveChanges], queries
        // included (Signature365 8b40ecc961, Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29).
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var unitOfWork = new TestUnitOfWork();
        var serviceProvider = new PersistenceTestServiceProvider(unitOfWork);

        var pipeline = executor.GetPipeline(typeof(PersistenceQueryProof));
        var context = new ApiOperationContext(serviceProvider, app, PersistenceQueryProof.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
        unitOfWork.SaveChangesCallCount.Should().Be(1, "save-changes should be called exactly once after a successful query, as after a command");
    }

    [Fact]
    public async Task When_command_has_DoNotSaveChanges_then_unit_of_work_is_not_invoked()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var unitOfWork = new TestUnitOfWork();
        var serviceProvider = new PersistenceTestServiceProvider(unitOfWork);

        var pipeline = executor.GetPipeline(typeof(DoNotSaveChangesCommand));
        var context = new ApiOperationContext(serviceProvider, app, DoNotSaveChangesCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((DoNotSaveChangesCommand)context.Operation).Name = "should-not-save";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();
        unitOfWork.SaveChangesCallCount.Should().Be(0, "save-changes should NOT be called when [DoNotSaveChanges] is applied");
    }

    private sealed class PersistenceTestServiceProvider : IServiceProvider
    {
        private readonly IUnitOfWork? _unitOfWork;

        public PersistenceTestServiceProvider(IUnitOfWork? unitOfWork)
        {
            this._unitOfWork = unitOfWork;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IUnitOfWork))
            {
                return this._unitOfWork;
            }

            return null;
        }
    }
}

/// <summary>
/// Test unit-of-work that records save-changes calls for assertion.
/// </summary>
public sealed class TestUnitOfWork : IUnitOfWork
{
    /// <summary>Gets the number of times SaveChangesAsync was called.</summary>
    public int SaveChangesCallCount { get; private set; }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A command operation that participates in persistence coordination.
/// Proves that the generated pipeline calls IUnitOfWork.SaveChangesAsync on success.
/// </summary>
public partial class PersistableCommand : ICommand
{
    /// <summary>Gets or sets the required name field (triggers validation when null).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(PersistableCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A query operation that proves the unit-of-work module participates in query pipelines, as legacy's save did (R138).
/// </summary>
public partial class PersistenceQueryProof : IQuery<string>
{
    /// <summary>Handles the query and returns a string result.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>A string result.</returns>
    public static Task<string> HandleAsync(PersistenceQueryProof operation)
    {
        return Task.FromResult("query-result");
    }
}

/// <summary>
/// A command with <c>[DoNotSaveChanges]</c> that proves the opt-out marker
/// prevents <c>UnitOfWorkModule</c> from contributing to the pipeline.
/// </summary>
[Blueprint.Persistence.DoNotSaveChanges]
public partial class DoNotSaveChangesCommand : ICommand
{
    /// <summary>Gets or sets the name field.</summary>
    public string? Name { get; set; }

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(DoNotSaveChangesCommand operation)
    {
        return Task.CompletedTask;
    }
}
