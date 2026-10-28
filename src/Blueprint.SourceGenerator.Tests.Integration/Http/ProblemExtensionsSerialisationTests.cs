using System.Text;
using System.Text.Json;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a problem's extensions are written whatever their runtime type, although the app's serializer context
/// registers none of them.
/// </summary>
/// <remarks>
/// <see cref="ProblemDetails.Extensions" /> is <c>Dictionary&lt;string, object?&gt;</c>, open by design: Signature365 puts a
/// <c>List&lt;TemplateCompilationError&gt;</c> in it for a template syntax error, and an error proxied from another service
/// arrives with <see cref="JsonElement" /> values. With the context as the only resolver, writing such a 400 threw and the
/// response became a 500 (Symprex/blueprint#94).
/// </remarks>
public sealed class ProblemExtensionsSerialisationTests
{
    private const string ExpectedErrors = """"errors":[{"line":3,"message":"Unexpected token"}]"""";

    [Fact]
    public async Task A_problem_written_by_the_executor_writes_extensions_of_unregistered_types()
    {
        using var services = CreateServices();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var httpContext = CreateHttpContext(services);

        await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, app, CreateProblem());

        var body = ReadBody(httpContext);
        body.Should().Contain(ExpectedErrors);
        body.Should().Contain(""""proxied":{"code":"upstream"}"""");
    }

    [Fact]
    public async Task A_problem_written_by_the_pipelines_inline_writer_writes_extensions_of_unregistered_types()
    {
        using var services = CreateServices();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None);
        var httpContext = CreateHttpContext(services);

        await PipelineHelpers.WriteOkJsonResponseAsync(httpContext, context, CreateProblem());

        var body = ReadBody(httpContext);
        body.Should().Contain(ExpectedErrors);
        body.Should().Contain(""""proxied":{"code":"upstream"}"""");
    }

    [Fact]
    public async Task An_unregistered_problem_subclass_written_by_the_pipelines_inline_writer_writes_its_extensions()
    {
        using var services = CreateServices();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None);
        var httpContext = CreateHttpContext(services);

        var problem = new TemplateProblem
        {
            Status = 400,
            Extensions = { ["errors"] = new List<CompilationError> { new(3, "Unexpected token") } },
        };

        await PipelineHelpers.WriteOkJsonResponseAsync(httpContext, context, problem);

        ReadBody(httpContext).Should().Contain(ExpectedErrors);
    }

    [Fact]
    public async Task An_ApiExceptions_extensions_of_unregistered_types_are_written_with_its_status()
    {
        using var services = CreateServices();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None);
        var httpContext = CreateHttpContext(services);

        var exception = new ApiException(
            "Template has syntax errors",
            "template_syntax_error",
            null,
            400,
            new Dictionary<string, object>
            {
                ["errors"] = new List<CompilationError> { new(3, "Unexpected token") },
                ["lines"] = new[] { 3, 7 },
            });

        await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, new UnhandledExceptionResult(exception));

        httpContext.Response.StatusCode.Should().Be(400);
        var body = ReadBody(httpContext);
        body.Should().Contain(ExpectedErrors);
        body.Should().Contain(""""lines":[3,7]"""");
    }

    [Fact]
    public async Task A_resource_event_returned_as_itself_writes_metadata_of_unregistered_types()
    {
        // A ResourceEvent returned as itself, not as a ResourceEventResult, is written as a declared root rather than
        // through the envelope writer, which already wrote these dictionaries with the fallback options.
        using var services = CreateServices();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None);
        var httpContext = CreateHttpContext(services);

        var resourceEvent = new Blueprint.Http.ResourceEvent<SchemaNameCollisions.EnvelopeOne.EnvelopeMatch>(
            Blueprint.Http.ResourceEventChangeType.Updated,
            "envelopeMatchUpdated",
            new SchemaNameCollisions.EnvelopeOne.EnvelopeMatch());
        resourceEvent.WithMetadata("errors", new List<CompilationError> { new(3, "Unexpected token") });

        await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, resourceEvent);

        ReadBody(httpContext).Should().Contain(ExpectedErrors);
    }

    private static ProblemDetails CreateProblem()
    {
        using var proxied = JsonDocument.Parse("""{"code":"upstream"}""");

        return new ProblemDetails
        {
            Status = 400,
            Title = "Template has syntax errors",
            Extensions =
            {
                ["errors"] = new List<CompilationError> { new(3, "Unexpected token") },
                ["proxied"] = proxied.RootElement.Clone(),
            },
        };
    }

    private static ServiceProvider CreateServices()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>()
            .BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
    }

    private static string ReadBody(HttpContext httpContext)
    {
        return Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
    }

    private sealed record CompilationError(int Line, string Message);

    private sealed class TemplateProblem : ProblemDetails
    {
        public string? Template { get; set; }
    }
}
