using System.Net;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// A command whose resource event has a self query saves its unit of work before the query runs, so the query reads what
/// it wrote. That save used to be made inside the self query's best-effort catch, which swallowed its refusal, so the
/// command succeeded when its later save went through (PR #198 review). A refused save there is the operation's 409 too.
/// </summary>
public class SaveRefusedSelfQueryIntegrationTests
{
    [Theory]
    [InlineData(typeof(LegacySelfQuerySaveRefusedCommand), "self_query_widget_conflict")]
    [InlineData(typeof(DeferredSelfQuerySaveRefusedCommand), "self_query_widget_conflict")]
    [InlineData(typeof(DeferredSelfQueryDefaultSaveRefusedCommand), "save_refused")]
    public async Task A_save_refused_before_the_self_query_is_answered_with_the_operations_409(Type operationType, string expectedType)
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var app = provider.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(operationType), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await provider.GetRequiredService<BlueprintAppExecutor>().GetPipeline(operationType).ExecuteAsync(context);

        var problem = result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Subject;
        problem.HttpStatus.Should().Be(409);
        problem.Type.Should().Be(expectedType);
        problem.InnerException.Should().BeOfType<SaveRefusedException>();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IntegrationTestBlueprintApp>();
        services.AddSingleton<BlueprintAppExecutor>(static sp =>
            new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, sp.GetRequiredService<IntegrationTestBlueprintApp>(), sp));
        services.AddScoped<IUnitOfWork, FirstSaveRefusedUnitOfWork>();

        return services.BuildServiceProvider();
    }

    /// <summary>Refuses the first save, as another request writing first would, and lets any later one through.</summary>
    private sealed class FirstSaveRefusedUnitOfWork : IUnitOfWork
    {
        private int _saves;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return ++this._saves == 1
                ? Task.FromException(new SaveRefusedException("refused", new InvalidOperationException("the store's own")))
                : Task.CompletedTask;
        }
    }
}

/// <summary>A command returning a legacy resource event with a self query, naming its own error for a refused save.</summary>
[SaveRefusedError(nameof(Conflict))]
public partial class LegacySelfQuerySaveRefusedCommand : ICommand<ResourceEvent<SelfQueryTestApiResource>>
{
    /// <summary>The widget was changed by another request.</summary>
    public static readonly ApiExceptionFactory Conflict = new("Widget conflict", "self_query_widget_conflict", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>An updated event left to its self query.</returns>
    public static ResourceEvent<SelfQueryTestApiResource> Handle(LegacySelfQuerySaveRefusedCommand operation)
    {
        return new ResourceEvent<SelfQueryTestApiResource>(
            ResourceEventChangeType.Updated,
            "updated",
            new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>A command returning a deferred resource event, naming its own error for a refused save.</summary>
[SaveRefusedError(nameof(Conflict))]
public partial class DeferredSelfQuerySaveRefusedCommand : ICommand<DeferredResourceEventResult<SelfQueryTestApiResource>>
{
    /// <summary>The widget was changed by another request.</summary>
    public static readonly ApiExceptionFactory Conflict = new("Widget conflict", "self_query_widget_conflict", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A deferred event left to its self query.</returns>
    public static DeferredResourceEventResult<SelfQueryTestApiResource> Handle(DeferredSelfQuerySaveRefusedCommand operation)
    {
        return new SelfQueryTestResourceEvents.Created(new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>A command returning a deferred resource event, naming no error for a refused save.</summary>
public partial class DeferredSelfQueryDefaultSaveRefusedCommand : ICommand<DeferredResourceEventResult<SelfQueryTestApiResource>>
{
    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A deferred event left to its self query.</returns>
    public static DeferredResourceEventResult<SelfQueryTestApiResource> Handle(DeferredSelfQueryDefaultSaveRefusedCommand operation)
    {
        return new SelfQueryTestResourceEvents.Created(new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}
