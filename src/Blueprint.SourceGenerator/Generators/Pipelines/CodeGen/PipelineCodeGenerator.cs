using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the executor pipeline code for a single operation.
/// </summary>
internal static class PipelineCodeGenerator
{
    /// <summary>
    /// Generates the complete pipeline class for an operation handler using the supplied exception-translation policy.
    /// </summary>
    /// <param name="handler">The discovered handler model that drives generated type and method names.</param>
    /// <param name="plan">The contributor-built plan that determines the rendered pipeline steps.</param>
    /// <param name="exceptionTranslatorPolicy">The validated exception translator policy that owns the generated catch shell.</param>
    /// <returns>The generated pipeline source for the supplied handler.</returns>
    public static string GeneratePipeline(OperationHandlerModel handler, PipelinePlan plan, PipelineExceptionTranslatorPolicy exceptionTranslatorPolicy)
    {
        var codeBuilder = CodeBuilder.Create("Blueprint.Generated");

        var pipelineClass = codeBuilder.AddClass(handler.PipelineClassName)
            .MakePublicClass()
            .AddInterface("Blueprint.IOperationExecutorPipeline");

        AddLoggingFields(pipelineClass);
        AddLoggerField(pipelineClass);
        AddConstructor(pipelineClass, handler);
        AddExecuteAsyncMethod(pipelineClass, handler, plan, exceptionTranslatorPolicy);

        // Modules register class-level helper methods (e.g. PopulateFromJsonBody) during
        // planning via PipelinePlan.AddDeferredClassMember. Apply them here so they are
        // present when ClassBuilder materialises its method list during Build().
        foreach (var member in plan.DeferredClassMembers)
        {
            member(pipelineClass);
        }

        return codeBuilder.Build();
    }

    private static void AddLoggingFields(ClassBuilder pipelineClass)
    {
        AddLoggingField(
            pipelineClass,
            "_ApiOperationExecuting",
            "Debug",
            1,
            "ApiOperationExecuting",
            "Executing API operation {OperationType} with handler");
        AddLoggingField(
            pipelineClass,
            "_NonException",
            "Information",
            4,
            "Non500Exception",
            "A non-5xx ApiException has occurred with message {ExceptionMessage}");
        AddLoggingField(
            pipelineClass,
            "_UnhandledException",
            "Error",
            5,
            "UnhandledException",
            "An unhandled exception has occurred with message {ExceptionMessage}");
    }

    private static void AddLoggingField(
        ClassBuilder pipelineClass,
        string fieldName,
        string logLevel,
        int eventId,
        string eventName,
        string message)
    {
        pipelineClass.AddProperty(fieldName)
            .MakePrivateProperty()
            .MakeStatic()
            .SetType("System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception>")
            .WithReadonlyValue(
                $"Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(Microsoft.Extensions.Logging.LogLevel.{logLevel}, new Microsoft.Extensions.Logging.EventId({eventId}, \"{eventName}\"), \"{message}\")");
    }

    private static void AddLoggerField(ClassBuilder pipelineClass)
    {
        pipelineClass.AddProperty("_logger")
            .MakePrivateProperty()
            .SetType("Microsoft.Extensions.Logging.ILogger")
            .WithReadonlyValue();
    }

    private static void AddConstructor(ClassBuilder pipelineClass, OperationHandlerModel handler)
    {
        pipelineClass.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .WithBody(writer => writer.Assign("_logger", $"loggerFactory.CreateLogger(\"{handler.PipelineClassName}\")"));
    }

    private static void AddExecuteAsyncMethod(
        ClassBuilder pipelineClass,
        OperationHandlerModel handler,
        PipelinePlan plan,
        PipelineExceptionTranslatorPolicy exceptionTranslatorPolicy)
    {
        var executeMethod = pipelineClass.AddMethod("ExecuteAsync")
            .MakePublicMethod()
            .WithReturnType("System.Threading.Tasks.Task<Blueprint.OperationResult>")
            .AddParameter("Blueprint.Runtime.Exec.ApiOperationContext", "context");

        if (handler.IsHandlerAsync)
        {
            executeMethod.MakeAsync();
        }
        else if (plan.RequiresAsync)
        {
            executeMethod.MakeAsync();
        }

        executeMethod.WithBody(
            writer =>
            {
                writer.AppendLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
                writer.DeclareVar("operation", $"({handler.OperationTypeFullName})context.Operation");
                writer.NewLine();

                writer.Try(
                    tryWriter =>
                    {
                        tryWriter.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
                        tryWriter.NewLine();
                        WritePipelineSteps(tryWriter, handler, plan);
                    });

                exceptionTranslatorPolicy.WriteExceptionHandling(writer, handler.IsHandlerAsync);
            });
    }

    private static void WritePipelineSteps(ICodeWriter writer, OperationHandlerModel handler, PipelinePlan plan)
    {
        foreach (var contribution in plan.Contributions)
        {
            foreach (var renderStep in plan.GetRenderSteps(contribution))
            {
                renderStep.Render(new PipelineRenderContext(writer, handler, plan));
            }
        }
    }

    /// <summary>
    /// Wraps a generated result expression for synchronous handlers so the surrounding execute method still returns a task.
    /// </summary>
    /// <param name="operationResultExpression">The generated operation-result expression.</param>
    /// <param name="isHandlerAsync">Whether the enclosing handler already returns asynchronously.</param>
    /// <returns>The expression to emit from the generated catch or handler-return path.</returns>
    internal static string WrapOperationResult(string operationResultExpression, bool isHandlerAsync)
    {
        return isHandlerAsync
            ? operationResultExpression
            : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})";
    }
}
