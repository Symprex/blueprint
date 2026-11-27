using System.Net;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Issue #197: a unit of work save refused because another request wrote first used to leave the pipeline as the store's
/// exception, which the caller got as a 500. The generated pipeline answers it with a 409 problem: the operation's own,
/// named with <c>[SaveRefusedError]</c>, or Blueprint's <c>save_refused</c>.
/// </summary>
public class SaveRefusedIntegrationTests
{
    [Fact]
    public async Task A_refused_save_of_an_operation_naming_no_error_is_answered_with_blueprints_409()
    {
        var refusal = new SaveRefusedException("refused", new InvalidOperationException("the store's own"));

        var result = await ExecuteAsync<SaveRefusedDefaultCommand>(SaveRefusedDefaultCommand.Configure(), new RefusingUnitOfWork(refusal));

        var problem = result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Subject;
        problem.HttpStatus.Should().Be(409);
        problem.Type.Should().Be("save_refused");
        problem.InnerException.Should().BeSameAs(refusal);
    }

    [Fact]
    public async Task A_refused_save_of_an_operation_naming_its_error_is_answered_with_that_error()
    {
        var refusal = new SaveRefusedException("refused", new InvalidOperationException("the store's own"));

        var result = await ExecuteAsync<SaveRefusedNamedCommand>(SaveRefusedNamedCommand.Configure(), new RefusingUnitOfWork(refusal));

        var problem = result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Subject;
        problem.HttpStatus.Should().Be(409);
        problem.Type.Should().Be("widget_concurrent_update");
        problem.Title.Should().Be("Widget changed concurrently");
        problem.InnerException.Should().BeSameAs(refusal);
    }

    [Fact]
    public async Task A_refused_save_named_by_a_getter_backed_property_is_answered_with_that_error()
    {
        var result = await ExecuteAsync<SaveRefusedGetterCommand>(SaveRefusedGetterCommand.Configure(), new RefusingUnitOfWork(new SaveRefusedException("refused", new InvalidOperationException())));

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>()
            .Which.Type.Should().Be("widget_claimed");
    }

    [Fact]
    public async Task Any_other_failure_of_the_save_is_not_answered_as_a_refusal()
    {
        var failure = new InvalidOperationException("The database is down.");

        var result = await ExecuteAsync<SaveRefusedNamedCommand>(SaveRefusedNamedCommand.Configure(), new RefusingUnitOfWork(failure));

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeSameAs(failure);
    }

    private static async Task<OperationResult> ExecuteAsync<T>(ApiOperationDescriptor descriptor, IUnitOfWork unitOfWork)
        where T : IOperation
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var context = new ApiOperationContext(new UnitOfWorkServiceProvider(unitOfWork), app, descriptor, CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        return await executor.GetPipeline(typeof(T)).ExecuteAsync(context);
    }

    private sealed class RefusingUnitOfWork(Exception failure) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException(failure);
        }
    }

    private sealed class UnitOfWorkServiceProvider(IUnitOfWork unitOfWork) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IUnitOfWork) ? unitOfWork : null;
        }
    }
}

/// <summary>A command that names no error for a refused save.</summary>
public partial class SaveRefusedDefaultCommand : ICommand
{
    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(SaveRefusedDefaultCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>A command that names its own error, a static field, for a refused save.</summary>
[SaveRefusedError(nameof(ConcurrentUpdate))]
public partial class SaveRefusedNamedCommand : ICommand
{
    /// <summary>The widget was changed by another request.</summary>
    public static readonly ApiExceptionFactory ConcurrentUpdate = new("Widget changed concurrently", "widget_concurrent_update", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(SaveRefusedNamedCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>A command that names its own error, a getter-backed property, for a refused save.</summary>
[SaveRefusedError(nameof(Claimed))]
public partial class SaveRefusedGetterCommand : ICommand
{
    /// <summary>Gets the error for a widget another request claimed first.</summary>
    public static ApiExceptionFactory Claimed => new("Widget claimed", "widget_claimed", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(SaveRefusedGetterCommand operation)
    {
        return Task.CompletedTask;
    }
}
