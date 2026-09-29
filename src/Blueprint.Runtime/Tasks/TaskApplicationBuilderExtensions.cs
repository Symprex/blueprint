using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Tasks;

// Matches ASP.NET Core's own namespace for IApplicationBuilder extensions, as legacy's did, so the call is discoverable
// wherever the pipeline is built.
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds the middleware that releases, after every request, the background tasks enqueued outside a Blueprint operation.
/// </summary>
public static class TaskApplicationBuilderExtensions
{
    /// <summary>
    /// Registers a middleware that, once the rest of the request pipeline has returned, calls
    /// <see cref="IBackgroundTaskScheduler.RunNowAsync"/> on the request's scheduler, so that tasks enqueued outside any
    /// Blueprint operation — from a Razor page, a controller or a service resolved from the request's services — are
    /// dispatched. Opt-in, as legacy's <c>UseBlueprintTaskRunner</c> was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It behaves as legacy's <c>TaskRunnerMiddleware</c> did (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint.Tasks.AspNetCore/TaskRunnerMiddleware.cs:32-36</c>): it awaits the rest of the pipeline and then releases
    /// the request's tasks, whatever status the request was answered with, and releases nothing when the rest of the
    /// pipeline throws. Register it so that the code enqueuing those tasks runs inside it, ahead of the endpoints, as
    /// Signature365's IdentityServer registered legacy's between <c>UseAuthorization</c> and <c>UseEndpoints</c>
    /// (<c>Startup.cs:370-376</c>).
    /// </para>
    /// <para>
    /// It releases nothing for a request routed to a Blueprint operation. Legacy's HTTP host ran each operation in a scope
    /// of its own (<c>Blueprint.Http/ApplicationBuilderExtensions.cs:213</c>), so the scheduler this middleware released
    /// never held an operation's tasks: the operation's pipeline released them when it succeeded, and nothing released them
    /// when it failed. This host runs an operation in the request's scope, so after an operation that failed — a validation
    /// failure, a handler that threw, a save that threw — its tasks are still in the request's scheduler, and releasing them
    /// here would dispatch work for changes that were never saved. Skipping the request keeps legacy's outcome for the
    /// operation's own tasks. It differs from legacy only for a task that code outside the operation enqueued during that
    /// same request, which legacy released and this does not.
    /// </para>
    /// <para>
    /// The request is recognised by its endpoint's <see cref="ApiOperationDescriptor"/> metadata, which every endpoint
    /// <c>MapBlueprintApi</c> maps carries, read once the rest of the pipeline has run.
    /// </para>
    /// <para>
    /// The scheduler is resolved from <see cref="HttpContext.RequestServices"/>, and nothing is released when none is
    /// registered (<see cref="PipelineHelpers.FlushBackgroundTasksAsync"/>), where legacy's middleware, whose
    /// <c>Invoke</c> took the scheduler as a parameter, failed every request. A release that throws surfaces as the request's
    /// exception, after the response may already have been sent.
    /// </para>
    /// </remarks>
    /// <param name="app">The application builder to register the middleware with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UseBlueprintTaskRunner(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static (context, next) => RunThenReleaseTasksAsync(context, next));
    }

    private static async Task RunThenReleaseTasksAsync(HttpContext context, RequestDelegate next)
    {
        await next(context).ConfigureAwait(false);

        if (context.GetEndpoint()?.Metadata.GetMetadata<ApiOperationDescriptor>() is not null)
        {
            return;
        }

        await PipelineHelpers.FlushBackgroundTasksAsync(context.RequestServices).ConfigureAwait(false);
    }
}
