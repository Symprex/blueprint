using System.Runtime.CompilerServices;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the response writers' per-serializer caches do not keep an app's serializer context alive once the app
/// itself is gone.
/// </summary>
/// <remarks>
/// The caches are static and keyed by the app's <see cref="System.Text.Json.Serialization.JsonSerializerContext" /> (or
/// its options). A process that builds an app per host - a test suite building a host per test is the common case -
/// otherwise keeps every app's context, options and reflection metadata until it exits. Signature365's Portal test shard
/// held 1,121 of them and outgrew a 16 GB CI runner.
/// </remarks>
public sealed class SerializerCacheLifetimeTests
{
    [Fact]
    public void When_an_app_has_written_responses_and_is_dropped_then_its_serializer_context_can_be_collected()
    {
        // Arrange: one response through each cache - a type the context covers, a type it does not (the fallback
        // options), and the pipeline's inline writer.
        var serializerContext = WriteResponsesWithAnAppAndDropIt();

        // Act
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        serializerContext.IsAlive.Should().BeFalse("no app references the serializer context any more");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WriteResponsesWithAnAppAndDropIt()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>()
            .BuildServiceProvider();

        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None);

        app.AppOwnedSerializerContext.Should().NotBeNull("the caches are keyed by the app's serializer context");

        var executor = new HttpOperationResultExecutor();

        executor.TryExecuteAsync(CreateHttpContext(services), context, new OkResult<LinkWireWidgetApiResource>(new LinkWireWidgetApiResource { Id = "covered" }))
            .GetAwaiter().GetResult();

        executor.TryExecuteAsync(CreateHttpContext(services), context, new OkResult<NotInTheSerializerContext>(new NotInTheSerializerContext("uncovered")))
            .GetAwaiter().GetResult();

        PipelineHelpers.WriteOkJsonResponseAsync(CreateHttpContext(services), context, new LinkWireWidgetApiResource { Id = "inline" })
            .GetAwaiter().GetResult();

        return new WeakReference(app.AppOwnedSerializerContext);
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
    }

    private sealed record NotInTheSerializerContext(string Name);
}
